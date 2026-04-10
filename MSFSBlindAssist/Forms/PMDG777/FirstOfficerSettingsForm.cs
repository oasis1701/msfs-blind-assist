using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.PMDG777;

/// <summary>
/// Simple settings dialog for First Officer automation options.
/// Changes are applied immediately to the provided UserSettings instance and persisted by the caller.
/// </summary>
public class FirstOfficerSettingsForm : Form
{
    private readonly UserSettings _settings;

    private CheckBox _autoGearCheck  = null!;
    private CheckBox _autoFlapsCheck = null!;
    private CheckBox _autoApCheck    = null!;
    private Button   _saveBtn        = null!;
    private Button   _cancelBtn      = null!;

    public FirstOfficerSettingsForm(UserSettings settings)
    {
        _settings = settings;
        BuildUI();
    }

    private void BuildUI()
    {
        Text           = "First Officer Settings";
        Width          = 420;
        Height         = 270;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox    = false;
        MinimizeBox    = false;
        StartPosition  = FormStartPosition.CenterParent;
        AccessibleName = "First Officer Settings";

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _autoGearCheck = new CheckBox
        {
            Text           = "Auto-manage gear (raise on positive rate; lower at 2000 ft AGL)",
            Checked        = _settings.FOAutoGearEnabled,
            AutoSize       = true,
            Margin         = new Padding(0, 0, 0, 8),
            AccessibleName = "Auto-manage gear",
            AccessibleDescription = "Automatically raise gear on positive rate and lower at 2000 feet AGL",
        };

        _autoFlapsCheck = new CheckBox
        {
            Text           = "Auto-manage flaps (retract on climbout; extend on approach)",
            Checked        = _settings.FOAutoFlapsEnabled,
            AutoSize       = true,
            Margin         = new Padding(0, 0, 0, 8),
            AccessibleName = "Auto-manage flaps",
            AccessibleDescription = "Automatically retract flaps on climbout and extend flaps on approach using FMC speeds",
        };

        _autoApCheck = new CheckBox
        {
            Text           = "Auto-engage autopilot at 500 ft AGL on climbout",
            Checked        = _settings.FOAutoApEnabled,
            AutoSize       = true,
            Margin         = new Padding(0, 0, 0, 8),
            AccessibleName = "Auto-engage autopilot",
            AccessibleDescription = "Automatically engage autopilot when climbing through 500 feet AGL after takeoff",
        };

        var btnPanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents  = false,
            AutoSize      = true,
            Margin        = new Padding(0),
        };

        _cancelBtn = new Button
        {
            Text           = "Cancel",
            AccessibleName = "Cancel",
            DialogResult   = DialogResult.Cancel,
            Margin         = new Padding(6, 0, 0, 0),
        };

        _saveBtn = new Button
        {
            Text           = "Save",
            AccessibleName = "Save settings",
            DialogResult   = DialogResult.OK,
        };
        _saveBtn.Click += (_, _) => ApplySettings();

        btnPanel.Controls.Add(_cancelBtn);
        btnPanel.Controls.Add(_saveBtn);

        layout.Controls.Add(_autoGearCheck,  0, 0);
        layout.Controls.Add(_autoFlapsCheck, 0, 1);
        layout.Controls.Add(_autoApCheck,    0, 2);
        layout.Controls.Add(new Panel(),     0, 3); // spacer
        layout.Controls.Add(btnPanel,        0, 4);

        Controls.Add(layout);
        AcceptButton = _saveBtn;
        CancelButton = _cancelBtn;
    }

    private void ApplySettings()
    {
        _settings.FOAutoGearEnabled  = _autoGearCheck.Checked;
        _settings.FOAutoFlapsEnabled = _autoFlapsCheck.Checked;
        _settings.FOAutoApEnabled    = _autoApCheck.Checked;
    }
}
