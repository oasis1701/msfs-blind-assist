using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 FCU Altitude window: enter feet; Push/Pull; 100/1000 increment selector.
// (Unlike the A380 there is no MTRS/metric toggle — the real A320 FCU has no metric
// altitude button, so the window stays in feet.)
public class FBWA320AltitudeWindow : FBWA320FCUWindowBase
{
    private readonly TextBox altTextBox;

    public FBWA320AltitudeWindow(FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set FCU Altitude";
        Size = new Size(420, 235);

        var label = new Label { Text = "Altitude (100-49000 ft):", Location = new Point(20, 25), Size = new Size(170, 20), AccessibleName = "Altitude Label" };
        altTextBox = new TextBox { Location = new Point(195, 22), Size = new Size(85, 25), TabIndex = 0, AccessibleName = "Altitude value" };
        altTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        var setButton = new Button { Text = "Set", Location = new Point(290, 20), Size = new Size(80, 30), TabIndex = 1, AccessibleName = "Set Altitude" };
        setButton.Click += (s, e) => HandleSet();
        var pushButton = new Button { Text = "Altitude Push (managed)", Location = new Point(20, 65), Size = new Size(175, 35), TabIndex = 2, AccessibleName = "Altitude Push" };
        pushButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_ALT_PUSH", simConnect, announcer, readback: false);
        var pullButton = new Button { Text = "Altitude Pull (selected)", Location = new Point(205, 65), Size = new Size(175, 35), TabIndex = 3, AccessibleName = "Altitude Pull" };
        pullButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_ALT_PULL", simConnect, announcer, readback: false);
        var inc100 = new Button { Text = "Increment 100", Location = new Point(20, 110), Size = new Size(175, 35), TabIndex = 4, AccessibleName = "Increment 100 feet" };
        inc100.Click += (s, e) => aircraft.SetAltIncrement(100, simConnect);
        var inc1000 = new Button { Text = "Increment 1000", Location = new Point(205, 110), Size = new Size(175, 35), TabIndex = 5, AccessibleName = "Increment 1000 feet" };
        inc1000.Click += (s, e) => aircraft.SetAltIncrement(1000, simConnect);
        var closeButton = new Button { Text = "Close", Location = new Point(140, 155), Size = new Size(140, 35), TabIndex = 6, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { label, altTextBox, setButton, pushButton, pullButton, inc100, inc1000, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;
    }

    // Fenix-style silent open (see FBWA320SpeedWindow): no stale-then-fresh readout.
    protected override void SpeakInitialReadout() { altTextBox.Focus(); }

    private void HandleSet()
    {
        string input = altTextBox.Text.Trim();
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out double v)) { announcer.AnnounceImmediate("Invalid number format"); altTextBox.SelectAll(); return; }
        if (v < 100 || v > 49000) { announcer.AnnounceImmediate("Altitude must be between 100 and 49000 feet"); altTextBox.SelectAll(); return; }
        aircraft.SetFCUAltitudeValue(v, simConnect, announcer);
        // SelectAll keeps the field populated and gives NVDA's "<value> selected" echo,
        // alongside the spoken "FCU altitude <value>, selected" readback — matching the A380.
        altTextBox.SelectAll();
    }
}
