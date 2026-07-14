using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>First Officer section of the unified Settings dialog. Extracted from the retired
/// standalone First Officer Settings dialog — the automation checkboxes (gear up/down, flaps,
/// AP, 10k lights) and the same UserSettings bindings, but the old Save/Cancel buttons are
/// gone (the dialog owns OK/Cancel)
/// and there is no live tone/resource to tear down, so <see cref="OnLeaving"/> is a no-op.
/// These are global preferences that only take effect when a supported First Officer window
/// (PMDG 777/737, Fenix A320, FBW A380) is open; the tab is shown for every aircraft.</summary>
public class FirstOfficerPanel : UserControl, ISettingsPanel
{
    private Label titleLabel = null!;
    private CheckBox autoGearUpCheck = null!;
    private CheckBox autoGearDownCheck = null!;
    private CheckBox autoFlapsCheck = null!;
    private CheckBox autoApCheck = null!;
    private Label apAltitudeLabel = null!;
    private TextBox apAltitudeText = null!;
    private CheckBox autoLights10kCheck = null!;
    private Label autoSeatbeltLabel = null!;
    private ComboBox autoSeatbeltCombo = null!;
    private Label gpwsTestLabel = null!;
    private ComboBox gpwsTestCombo = null!;

    public string TabTitle => "First Officer";

    public FirstOfficerPanel()
    {
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        titleLabel = new Label
        {
            Text = "Configure First Officer automation options:",
            Location = new Point(20, 20),
            Size = new Size(450, 20),
            AccessibleName = "First Officer Settings Title"
        };

        // AutoSize (matching the retired standalone dialog) so labels grow with the system
        // font instead of clipping at >100% Windows text scaling — the app sets no
        // AutoScaleDimensions, so fixed pixel widths never rescale while fonts do.
        autoGearUpCheck = new CheckBox
        {
            Text = "Auto-raise gear on positive rate (climb)",
            Location = new Point(20, 55),
            AutoSize = true,
            AccessibleName = "Auto-raise gear on climb",
            AccessibleDescription = "Automatically raise the landing gear on positive rate after takeoff"
        };

        autoGearDownCheck = new CheckBox
        {
            Text = "Auto-lower gear at 2000 ft AGL (descent)",
            Location = new Point(20, 90),
            AutoSize = true,
            AccessibleName = "Auto-lower gear on descent",
            AccessibleDescription = "Automatically lower the landing gear when descending through 2000 feet AGL"
        };

        autoFlapsCheck = new CheckBox
        {
            Text = "Auto-manage flaps (FBW A380 and A32NX)",
            Location = new Point(20, 125),
            AutoSize = true,
            AccessibleName = "Auto-manage flaps",
            AccessibleDescription = "Automatically extend and retract flaps on the FlyByWire A380 and A32NX using the published speed-tape values. Has no effect on other aircraft."
        };

        autoApCheck = new CheckBox
        {
            Text = "Auto-engage autopilot on climbout",
            Location = new Point(20, 160),
            AutoSize = true,
            AccessibleName = "Auto-engage autopilot",
            AccessibleDescription = "Automatically engage the autopilot when climbing through the engagement altitude after takeoff"
        };

        apAltitudeLabel = new Label
        {
            Text = "Autopilot engagement altitude (feet AGL):",
            Location = new Point(20, 195),
            AutoSize = true,
            AccessibleName = "Autopilot engagement altitude label"
        };

        apAltitudeText = new TextBox
        {
            Location = new Point(320, 192),
            Size = new Size(80, 23),
            AccessibleName = "Autopilot engagement altitude in feet AGL",
            AccessibleDescription = "Height above ground at which the First Officer engages the autopilot on climbout. 100 to 3000 feet."
        };

        autoLights10kCheck = new CheckBox
        {
            Text = "Automatically switch landing lights at 10,000 feet",
            Location = new Point(20, 230),
            AutoSize = true,
            AccessibleName = "Automatic landing lights at ten thousand feet",
            AccessibleDescription = "Turn the landing lights (and nose light where fitted) off climbing through 10,000 feet and on descending through it"
        };

        autoSeatbeltLabel = new Label
        {
            Text = "Auto seat belt signs:",
            Location = new Point(20, 265),
            AutoSize = true,
            AccessibleName = "Auto seat belt signs label"
        };

        autoSeatbeltCombo = new ComboBox
        {
            Location = new Point(200, 262),
            Size = new Size(240, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Auto seat belt signs mode",
            AccessibleDescription = "When to switch the seat belt signs automatically using the aircraft's own control. Disabled; at 10,000 feet; or off at top of climb and on at top of descent."
        };
        autoSeatbeltCombo.Items.AddRange(new object[]
        {
            "Disabled",
            "At 10,000 feet",
            "Top of climb and top of descent"
        });

        gpwsTestLabel = new Label
        {
            Text = "GPWS test type (PMDG 737):",
            Location = new Point(20, 300),
            AutoSize = true,
            AccessibleName = "GPWS test type label"
        };

        gpwsTestCombo = new ComboBox
        {
            Location = new Point(240, 297),
            Size = new Size(200, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "GPWS test type",
            AccessibleDescription = "Which GPWS self-test the 737 First Officer preflight flow and checklist run: the short test or the long extended test. Both are available as panel buttons regardless."
        };
        gpwsTestCombo.Items.AddRange(new object[] { "Short test", "Long test" });

        Controls.AddRange(new Control[]
        {
            titleLabel, autoGearUpCheck, autoGearDownCheck, autoFlapsCheck, autoApCheck,
            apAltitudeLabel, apAltitudeText, autoLights10kCheck,
            autoSeatbeltLabel, autoSeatbeltCombo, gpwsTestLabel, gpwsTestCombo
        });
    }

    private void SetupAccessibility()
    {
        titleLabel.TabIndex = 0;
        autoGearUpCheck.TabIndex = 1;
        autoGearDownCheck.TabIndex = 2;
        autoFlapsCheck.TabIndex = 3;
        autoApCheck.TabIndex = 4;
        apAltitudeLabel.TabIndex = 5;
        apAltitudeText.TabIndex = 6;
        autoLights10kCheck.TabIndex = 7;
        autoSeatbeltLabel.TabIndex = 8;
        autoSeatbeltCombo.TabIndex = 9;
        gpwsTestLabel.TabIndex = 10;
        gpwsTestCombo.TabIndex = 11;
    }

    public void LoadFrom(UserSettings settings)
    {
        autoGearUpCheck.Checked = settings.FOAutoGearUpEnabled;
        autoGearDownCheck.Checked = settings.FOAutoGearDownEnabled;
        autoFlapsCheck.Checked = settings.FOAutoFlapsEnabled;
        autoApCheck.Checked = settings.FOAutoApEnabled;
        apAltitudeText.Text = settings.FOAutoApEngageAltitudeAgl.ToString();
        autoLights10kCheck.Checked = settings.FOAutoLights10kEnabled;
        autoSeatbeltCombo.SelectedIndex = Math.Clamp(settings.FOAutoSeatbeltMode, 0, 2);
        gpwsTestCombo.SelectedIndex = settings.FOGpws737LongTest ? 1 : 0;
    }

    public bool Validate(out string error, out Control? focus)
    {
        if (!(int.TryParse(apAltitudeText.Text, out int apAlt) && apAlt >= 100 && apAlt <= 3000))
        {
            error = "Please enter an autopilot engagement altitude between 100 and 3000 feet AGL.";
            focus = apAltitudeText;
            return false;
        }
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.FOAutoGearUpEnabled = autoGearUpCheck.Checked;
        settings.FOAutoGearDownEnabled = autoGearDownCheck.Checked;
        settings.FOAutoFlapsEnabled = autoFlapsCheck.Checked;
        settings.FOAutoApEnabled = autoApCheck.Checked;
        settings.FOAutoApEngageAltitudeAgl = int.Parse(apAltitudeText.Text);
        settings.FOAutoLights10kEnabled = autoLights10kCheck.Checked;
        settings.FOAutoSeatbeltMode = autoSeatbeltCombo.SelectedIndex < 0 ? 0 : autoSeatbeltCombo.SelectedIndex;
        settings.FOGpws737LongTest = gpwsTestCombo.SelectedIndex == 1;
    }

    /// <summary>No transient resources (no test tone) — nothing to stop when this tab is left.</summary>
    public void OnLeaving()
    {
    }
}
