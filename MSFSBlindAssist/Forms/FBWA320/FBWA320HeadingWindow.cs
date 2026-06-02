using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 FCU Heading window: enter 0-360 deg; Push/Pull; HDG·V/S <-> TRK·FPA toggle.
public class FBWA320HeadingWindow : FBWA320FCUWindowBase
{
    private readonly TextBox headingTextBox;

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
        pushButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_TO_AP_HDG_PUSH", simConnect, announcer);
        var pullButton = new Button { Text = "Heading Pull (selected)", Location = new Point(195, 65), Size = new Size(165, 35), TabIndex = 3, AccessibleName = "Heading Pull" };
        pullButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_TO_AP_HDG_PULL", simConnect, announcer);
        var trkButton = new Button { Text = "HDG·V/S / TRK·FPA toggle", Location = new Point(20, 110), Size = new Size(340, 35), TabIndex = 4, AccessibleName = "Track FPA toggle" };
        trkButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_TRK_FPA_TOGGLE_PUSH", simConnect, announcer);
        var closeButton = new Button { Text = "Close", Location = new Point(130, 155), Size = new Size(140, 35), TabIndex = 5, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { label, headingTextBox, setButton, pushButton, pullButton, trkButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;
    }

    protected override void SpeakInitialReadout() { aircraft.RequestFCUHeadingReadout(simConnect); headingTextBox.Focus(); }

    private void HandleSet()
    {
        string input = headingTextBox.Text.Trim();
        if (!double.TryParse(input, out double v)) { announcer.AnnounceImmediate("Invalid number format"); headingTextBox.SelectAll(); return; }
        if (v < 0 || v > 360) { announcer.AnnounceImmediate("Heading must be between 0 and 360 degrees"); headingTextBox.SelectAll(); return; }
        aircraft.SetFCUHeadingValue((int)Math.Round(v) % 360, simConnect, announcer);
        headingTextBox.SelectAll();
    }
}
