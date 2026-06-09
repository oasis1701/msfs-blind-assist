using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 FCU Heading window: enter 0-360 deg; Push/Pull; HDG·V/S <-> TRK·FPA toggle.
public class FBWA320HeadingWindow : FBWA320FCUWindowBase
{
    private readonly TextBox headingTextBox;
    private readonly Button trkButton;
    private System.Windows.Forms.Timer? _modeTimer;

    public FBWA320HeadingWindow(FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set FCU Heading";
        Size = new Size(400, 230);

        var label = new Label { Text = "Heading (0-360):", Location = new Point(20, 25), Size = new Size(140, 20), AccessibleName = "Heading Label" };
        headingTextBox = new TextBox { Location = new Point(195, 22), Size = new Size(75, 25), TabIndex = 0, AccessibleName = "Heading value", AccessibleDescription = "Enter 0 to 360 degrees" };
        headingTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        var setButton = new Button { Text = "Set", Location = new Point(280, 20), Size = new Size(80, 30), TabIndex = 1, AccessibleName = "Set Heading" };
        setButton.Click += (s, e) => HandleSet();
        var pushButton = new Button { Text = "Heading Push (managed)", Location = new Point(20, 65), Size = new Size(165, 35), TabIndex = 2, AccessibleName = "Heading Push" };
        pushButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_HDG_PUSH", simConnect, announcer, readback: false);
        var pullButton = new Button { Text = "Heading Pull (selected)", Location = new Point(195, 65), Size = new Size(165, 35), TabIndex = 3, AccessibleName = "Heading Pull" };
        pullButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_HDG_PULL", simConnect, announcer, readback: false);
        trkButton = new Button { Text = "HDG·V/S / TRK·FPA toggle", Location = new Point(20, 110), Size = new Size(340, 35), TabIndex = 4, AccessibleName = "Track FPA toggle" };
        trkButton.Click += (s, e) => { aircraft.FireFCUButton("A32NX.FCU_TRK_FPA_TOGGLE_PUSH", simConnect, announcer, readback: false); UpdateTrkLabel(); };
        var closeButton = new Button { Text = "Close", Location = new Point(130, 155), Size = new Size(140, 35), TabIndex = 5, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { label, headingTextBox, setButton, pushButton, pullButton, trkButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;

        // Reflect the live HDG·V/S vs TRK·FPA mode in the toggle button label.
        _modeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _modeTimer.Tick += (s, e) => UpdateTrkLabel();
    }

    // Fenix-style silent open (see FBWA320SpeedWindow): no stale-then-fresh readout.
    protected override void SpeakInitialReadout() { UpdateTrkLabel(); _modeTimer?.Start(); headingTextBox.Focus(); }

    private void UpdateTrkLabel()
    {
        bool isTrk = (simConnect.GetCachedVariableValue("A32NX_TRK_FPA_MODE_ACTIVE") ?? 0) > 0.5;
        string text = isTrk
            ? "TRK·FPA / HDG·V/S toggle — now TRK·FPA (press for HDG·V/S)"
            : "HDG·V/S / TRK·FPA toggle — now HDG·V/S (press for TRK·FPA)";
        if (trkButton.Text != text)
        {
            trkButton.Text = text;
            trkButton.AccessibleName = isTrk ? "Track FPA toggle, currently TRK FPA" : "Track FPA toggle, currently HDG V/S";
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { _modeTimer?.Stop(); _modeTimer?.Dispose(); _modeTimer = null; base.OnFormClosing(e); }

    private void HandleSet()
    {
        string input = headingTextBox.Text.Trim();
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out double v)) { announcer.AnnounceImmediate("Invalid number format"); headingTextBox.SelectAll(); return; }
        if (v < 0 || v > 360) { announcer.AnnounceImmediate("Heading must be between 0 and 360 degrees"); headingTextBox.SelectAll(); return; }
        aircraft.SetFCUHeadingValue((int)Math.Round(v) % 360, simConnect, announcer);
        headingTextBox.SelectAll();
    }
}
