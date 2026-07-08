using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// FMC settings dialog. Opened from the menu when either a PMDG aircraft
/// or the Fenix A320 is loaded (gated in MainForm). Hosts two toggles:
///
/// 1. Alternate LSK keys (applies to both PMDG 777 and Fenix A320) —
///    switches each aircraft's MCDU form line-select bindings from the
///    default <c>Ctrl+1..6</c> / <c>Alt+1..6</c> to <c>F1..F6</c> /
///    <c>F7..F12</c>. Frees Ctrl/Alt for other shortcuts.
///
/// 2. Enhanced distance announcement (PMDG only) — when on, the Output
///    <c>D</c> / <c>Shift+D</c> keys parse the PMDG PROG page for distance,
///    ETA in Z time, landing fuel, TOC and step-climb data. No effect on
///    the Fenix A320.
///
/// The form uses the property-return pattern: caller reads the public
/// properties after <c>ShowDialog() == DialogResult.OK</c> and writes them
/// back to <see cref="UserSettings"/>. The form does not touch settings
/// storage itself — that's the caller's responsibility.
/// </summary>
public class FMCSettingsForm : Form
{
    private CheckBox alternateLSKCheckBox = null!;
    private CheckBox enhancedDistanceCheckBox = null!;
    private Label alternateLSKDescription = null!;
    private Label enhancedDistanceDescription = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;

    public bool UseAlternateLSKKeys { get; private set; }
    public bool EnhancedDistanceMode { get; private set; }

    public FMCSettingsForm(bool currentUseAlternate, bool currentEnhancedDistance)
    {
        UseAlternateLSKKeys = currentUseAlternate;
        EnhancedDistanceMode = currentEnhancedDistance;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "FMC Settings";
        Size = new Size(560, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AcceptButton = null;
        CancelButton = null;

        var titleLabel = new Label
        {
            Text = "Configure FMC behaviour:",
            Location = new Point(20, 20),
            Size = new Size(500, 22),
            AccessibleName = "FMC settings title"
        };

        // ---- Alternate LSK keys ----
        alternateLSKCheckBox = new CheckBox
        {
            Text = "&Alternate line select keys (F1-F6 / F7-F12)",
            Location = new Point(20, 55),
            Size = new Size(480, 22),
            Checked = UseAlternateLSKKeys,
            AccessibleName = "Alternate line select keys",
            AccessibleDescription = "When checked, F1 through F6 select left line keys L1 to L6, " +
                                    "and F7 through F12 select right line keys R1 to R6. " +
                                    "Default is Control plus 1 to 6 for left, Alt plus 1 to 6 for right."
        };

        alternateLSKDescription = new Label
        {
            Text = "Default is Ctrl+1..6 = L1..L6 and Alt+1..6 = R1..R6. Switch to F1..F6 (left) and F7..F12 (right) to free Ctrl/Alt.",
            Location = new Point(40, 80),
            Size = new Size(490, 36),
            ForeColor = SystemColors.GrayText,
            AccessibleRole = AccessibleRole.StaticText,
            TabStop = false
        };

        // ---- Enhanced distance announcement ----
        enhancedDistanceCheckBox = new CheckBox
        {
            Text = "&Enhanced distance announcements (PMDG only)",
            Location = new Point(20, 130),
            Size = new Size(480, 22),
            Checked = EnhancedDistanceMode,
            AccessibleName = "Enhanced distance announcements, PMDG only",
            AccessibleDescription = "PMDG aircraft only. When checked, the Output D and Shift+D distance keys read the FMC progress page " +
                                    "to give distance to destination with E T A in Z time and landing fuel, " +
                                    "and distance to top of climb, step climb, or top of descent for Shift+D. " +
                                    "Falls back to the default readout if the progress page can't be activated. Has no effect on the Fenix A320."
        };

        enhancedDistanceDescription = new Label
        {
            Text = "PMDG only. Reads the PROG page on the right CDU. Requires the CDU to be powered. " +
                   "Output D = distance + ETA Z + landing fuel; Shift+D = T O C / step climb / T O D in NM + ETA Z.",
            Location = new Point(40, 155),
            Size = new Size(490, 50),
            ForeColor = SystemColors.GrayText,
            AccessibleRole = AccessibleRole.StaticText,
            TabStop = false
        };

        // ---- OK / Cancel ----
        okButton = new Button
        {
            Text = "&OK",
            Location = new Point(330, 250),
            Size = new Size(90, 28),
            DialogResult = DialogResult.OK,
            AccessibleName = "OK"
        };
        okButton.Click += (s, e) =>
        {
            UseAlternateLSKKeys = alternateLSKCheckBox.Checked;
            EnhancedDistanceMode = enhancedDistanceCheckBox.Checked;
        };

        cancelButton = new Button
        {
            Text = "&Cancel",
            Location = new Point(430, 250),
            Size = new Size(90, 28),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel"
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(titleLabel);
        Controls.Add(alternateLSKCheckBox);
        Controls.Add(alternateLSKDescription);
        Controls.Add(enhancedDistanceCheckBox);
        Controls.Add(enhancedDistanceDescription);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        // Tab order: checkbox 1 → checkbox 2 → OK → Cancel. Description
        // labels are TabStop=false so the screen reader doesn't dwell on them.
        int t = 0;
        alternateLSKCheckBox.TabIndex = t++;
        enhancedDistanceCheckBox.TabIndex = t++;
        okButton.TabIndex = t++;
        cancelButton.TabIndex = t++;
    }
}
