using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 FCU V/S–FPA window: enter +-6000 fpm or +-9.9 deg FPA (signed); Push/Pull.
public class FBWA320VSWindow : FBWA320FCUWindowBase
{
    private readonly TextBox vsTextBox;

    public FBWA320VSWindow(FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set FCU Vertical Speed / FPA";
        Size = new Size(430, 180);

        var label = new Label { Text = "V/S (+-6000) or FPA (+-9.9):", Location = new Point(20, 25), Size = new Size(190, 20), AccessibleName = "Vertical Speed Label" };
        vsTextBox = new TextBox { Location = new Point(215, 22), Size = new Size(85, 25), TabIndex = 0, AccessibleName = "Vertical speed value", AccessibleDescription = "Enter -6000 to 6000 feet per minute or -9.9 to 9.9 degrees FPA" };
        vsTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        var setButton = new Button { Text = "Set", Location = new Point(310, 20), Size = new Size(80, 30), TabIndex = 1, AccessibleName = "Set Vertical Speed" };
        setButton.Click += (s, e) => HandleSet();
        var pushButton = new Button { Text = "V/S Push (level off)", Location = new Point(20, 65), Size = new Size(180, 35), TabIndex = 2, AccessibleName = "Vertical Speed Push" };
        pushButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_VS_PUSH", simConnect, announcer, readback: false);
        var pullButton = new Button { Text = "V/S Pull (engage)", Location = new Point(210, 65), Size = new Size(180, 35), TabIndex = 3, AccessibleName = "Vertical Speed Pull" };
        pullButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_VS_PULL", simConnect, announcer, readback: false);
        var closeButton = new Button { Text = "Close", Location = new Point(145, 110), Size = new Size(140, 35), TabIndex = 4, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { label, vsTextBox, setButton, pushButton, pullButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;
    }

    // Fenix-style silent open (see FBWA320SpeedWindow): no stale-then-fresh readout.
    protected override void SpeakInitialReadout() { vsTextBox.Focus(); }

    private void HandleSet()
    {
        string input = vsTextBox.Text.Trim();
        if (!double.TryParse(input, out double v)) { announcer.AnnounceImmediate("Invalid number format"); vsTextBox.SelectAll(); return; }
        if (!((v >= -6000 && v <= 6000) || (v >= -9.9 && v <= 9.9))) { announcer.AnnounceImmediate("Value must be -6000 to 6000 ft/min or -9.9 to 9.9 degrees FPA"); vsTextBox.SelectAll(); return; }
        aircraft.SetFCUVSValue(v, simConnect, announcer);
        vsTextBox.SelectAll();
    }
}
