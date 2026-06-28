namespace MSFSBlindAssist.Forms;

/// <summary>
/// Tiny modal dialog to set the Waypoint Flight Director's heading/altitude "bug" (option 1) — e.g.
/// an ATC/FMC "fly heading 220, maintain 8000". Either field may be left blank. Both blank = clear
/// the bug (revert to following the tracked waypoint slots). Opened on input-mode Ctrl+F.
/// </summary>
public class FdBugForm : Form
{
    private TextBox headingBox = null!;
    private TextBox altitudeBox = null!;

    /// <summary>Parsed magnetic heading (0–360) or null if blank/invalid.</summary>
    public double? Heading { get; private set; }
    /// <summary>Parsed target altitude (feet MSL) or null if blank/invalid.</summary>
    public double? Altitude { get; private set; }

    public FdBugForm(double? currentHeading, double? currentAltitude)
    {
        Text = "Flight Director Heading / Altitude";
        Size = new Size(420, 210);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var headingLabel = new Label
        {
            Text = "Heading (magnetic, blank = none):",
            Location = new Point(20, 18),
            Size = new Size(360, 20),
            AccessibleName = "Heading label"
        };
        headingBox = new TextBox
        {
            Location = new Point(20, 40),
            Size = new Size(360, 23),
            Text = currentHeading.HasValue ? ((int)Math.Round(currentHeading.Value)).ToString() : "",
            AccessibleName = "Flight Director target heading in magnetic degrees, optional",
            AccessibleDescription = "Type a heading 0 to 360 to fly. Leave blank to keep the current lateral mode."
        };

        var altitudeLabel = new Label
        {
            Text = "Altitude (feet MSL, blank = none):",
            Location = new Point(20, 72),
            Size = new Size(360, 20),
            AccessibleName = "Altitude label"
        };
        altitudeBox = new TextBox
        {
            Location = new Point(20, 94),
            Size = new Size(360, 23),
            Text = currentAltitude.HasValue ? ((int)Math.Round(currentAltitude.Value)).ToString() : "",
            AccessibleName = "Flight Director target altitude to hold in feet MSL, optional",
            AccessibleDescription = "Type an altitude to capture and hold. Leave blank for no vertical command."
        };

        var okButton = new Button
        {
            Text = "OK",
            Location = new Point(215, 135),
            Size = new Size(80, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "OK",
            AccessibleDescription = "Apply the heading and/or altitude. Both blank clears the Flight Director bug."
        };
        okButton.Click += (s, e) => Commit();

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(300, 135),
            Size = new Size(80, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel"
        };

        Controls.AddRange(new Control[] { headingLabel, headingBox, altitudeLabel, altitudeBox, okButton, cancelButton });
        AcceptButton = okButton;
        CancelButton = cancelButton;

        headingLabel.TabIndex = 0;
        headingBox.TabIndex = 1;
        altitudeLabel.TabIndex = 2;
        altitudeBox.TabIndex = 3;
        okButton.TabIndex = 4;
        cancelButton.TabIndex = 5;

        Load += (s, e) => headingBox.Focus();
    }

    private void Commit()
    {
        Heading = null;
        Altitude = null;
        if (double.TryParse(headingBox.Text.Trim(), out double h) && h >= 0 && h <= 360)
            Heading = h % 360.0;
        if (double.TryParse(altitudeBox.Text.Trim(), out double a) && a >= -2000 && a <= 60000)
            Altitude = a;
    }
}
