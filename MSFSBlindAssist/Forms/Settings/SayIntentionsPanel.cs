using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>SayIntentions section of the unified Settings dialog. The API key is
/// optional — blank means "read it from flight.json during an active flight".</summary>
public class SayIntentionsPanel : UserControl, ISettingsPanel
{
    private Label instructionsLabel = null!;
    private Label apiKeyLabel = null!;
    private TextBox apiKeyTextBox = null!;
    private CheckBox autoStartTaxiGuidanceCheckBox = null!;

    public string TabTitle => "SayIntentions";

    public SayIntentionsPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        instructionsLabel = new Label
        {
            Text = "Optionally enter your SayIntentions API key to enable comms history and " +
                   "parking assignment lookups.\n\n" +
                   "Leave this blank to use the API key from flight.json during an active " +
                   "SayIntentions flight.",
            Location = new System.Drawing.Point(20, 20),
            Size = new System.Drawing.Size(440, 70),
            AccessibleName = "Instructions",
            AccessibleDescription = "SayIntentions settings instructions"
        };

        apiKeyLabel = new Label
        {
            Text = "SayIntentions API &key:",
            Location = new System.Drawing.Point(20, 100),
            Size = new System.Drawing.Size(200, 20),
            AccessibleName = "API key label"
        };

        // Deliberately NOT UseSystemPasswordChar — do not add it. NVDA and JAWS
        // refuse to speak a password field's contents, so a blind user could never
        // read back a pasted key to check it landed intact. The SimBrief username
        // and the Gemini/Claude API keys are plain text for the same reason; a key
        // the user cannot verify is worse than one a sighted onlooker could read.
        apiKeyTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, 125),
            Size = new System.Drawing.Size(440, 25),
            AccessibleName = "SayIntentions API key",
            AccessibleDescription = "Optional. Leave blank to read the key from flight.json."
        };

        autoStartTaxiGuidanceCheckBox = new CheckBox
        {
            Text = "Start taxi &guidance immediately after building a SayIntentions route",
            Location = new System.Drawing.Point(20, 165),
            Size = new System.Drawing.Size(440, 40),
            AccessibleName = "Start taxi guidance immediately",
            AccessibleDescription = "When cleared, the taxi guidance dialog opens with the route " +
                                    "fields filled in so you can review them before starting."
        };

        Controls.Add(instructionsLabel);
        Controls.Add(apiKeyLabel);
        Controls.Add(apiKeyTextBox);
        Controls.Add(autoStartTaxiGuidanceCheckBox);
    }

    public void LoadFrom(UserSettings settings)
    {
        apiKeyTextBox.Text = settings.SayIntentionsApiKey ?? "";
        autoStartTaxiGuidanceCheckBox.Checked = settings.SayIntentionsAutoStartTaxiGuidance;
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.SayIntentionsApiKey = apiKeyTextBox.Text.Trim();
        settings.SayIntentionsAutoStartTaxiGuidance = autoStartTaxiGuidanceCheckBox.Checked;
    }

    public void OnLeaving()
    {
    }
}
