using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

public partial class SayIntentionsSettingsForm : Form
{
    private Label instructionsLabel = null!;
    private Label apiKeyLabel = null!;
    private TextBox apiKeyTextBox = null!;
    private CheckBox autoStartTaxiGuidanceCheckBox = null!;
    private Button saveButton = null!;
    private Button cancelButton = null!;

    public SayIntentionsSettingsForm()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void InitializeComponent()
    {
        Text = "SayIntentions Settings";
        Size = new Size(560, 295);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        instructionsLabel = new Label
        {
            Text = "Optionally enter your SayIntentions API key. Leave this blank to use the API key from flight.json during an active SayIntentions flight.",
            Location = new Point(20, 20),
            Size = new Size(500, 50),
            AccessibleName = "Instructions",
            AccessibleDescription = "SayIntentions settings instructions"
        };

        apiKeyLabel = new Label
        {
            Text = "SayIntentions API &key:",
            Location = new Point(20, 85),
            AutoSize = true,
            AccessibleName = "SayIntentions API key label"
        };

        apiKeyTextBox = new TextBox
        {
            Location = new Point(20, 110),
            Size = new Size(500, 25),
            UseSystemPasswordChar = true,
            AccessibleName = "SayIntentions API key",
            AccessibleDescription = "Optional SayIntentions API key. Leave blank to read it from flight.json."
        };

        autoStartTaxiGuidanceCheckBox = new CheckBox
        {
            Text = "Start taxi &guidance immediately after building a SayIntentions route",
            Location = new Point(20, 145),
            Size = new Size(500, 25),
            AccessibleName = "Start taxi guidance immediately",
            AccessibleDescription = "When checked, the SayIntentions taxi route command starts guidance immediately. When unchecked, it opens the taxi guidance dialog with the route fields filled in for review."
        };

        saveButton = new Button
        {
            Text = "Save",
            Location = new Point(330, 205),
            Size = new Size(90, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Save",
            AccessibleDescription = "Save SayIntentions settings"
        };
        saveButton.Click += SaveButton_Click;

        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(430, 205),
            Size = new Size(90, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without saving"
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.Add(instructionsLabel);
        Controls.Add(apiKeyLabel);
        Controls.Add(apiKeyTextBox);
        Controls.Add(autoStartTaxiGuidanceCheckBox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);

        apiKeyTextBox.TabIndex = 0;
        autoStartTaxiGuidanceCheckBox.TabIndex = 1;
        saveButton.TabIndex = 2;
        cancelButton.TabIndex = 3;
    }

    private void LoadCurrentSettings()
    {
        apiKeyTextBox.Text = SettingsManager.Current.SayIntentionsApiKey ?? "";
        autoStartTaxiGuidanceCheckBox.Checked = SettingsManager.Current.SayIntentionsAutoStartTaxiGuidance;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        var settings = SettingsManager.Current;
        settings.SayIntentionsApiKey = apiKeyTextBox.Text.Trim();
        settings.SayIntentionsAutoStartTaxiGuidance = autoStartTaxiGuidanceCheckBox.Checked;
        SettingsManager.Save(settings);
    }
}
