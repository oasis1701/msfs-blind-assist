using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

// A380 FCU Altitude window: enter feet (or metres when MTRS on); Push/Pull;
// 100/1000 increment; Metric toggle. Respects aircraft.MetricAlt for input units.
public class FBWA380AltitudeWindow : FBWA380FCUWindowBase
{
    private readonly TextBox altTextBox;
    private readonly Label label;
    private readonly Button metricButton;

    public FBWA380AltitudeWindow(FlyByWireA380Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set FCU Altitude";
        Size = new Size(420, 280);

        label = new Label { Location = new Point(20, 25), Size = new Size(170, 20), AccessibleName = "Altitude Label" };
        altTextBox = new TextBox { Location = new Point(195, 22), Size = new Size(85, 25), TabIndex = 0, AccessibleName = "Altitude value" };
        altTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        var setButton = new Button { Text = "Set", Location = new Point(290, 20), Size = new Size(80, 30), TabIndex = 1, AccessibleName = "Set Altitude" };
        setButton.Click += (s, e) => HandleSet();
        var pushButton = new Button { Text = "Altitude Push (managed)", Location = new Point(20, 65), Size = new Size(175, 35), TabIndex = 2, AccessibleName = "Altitude Push" };
        pushButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_ALT_PUSH", simConnect, announcer);
        var pullButton = new Button { Text = "Altitude Pull (selected)", Location = new Point(205, 65), Size = new Size(175, 35), TabIndex = 3, AccessibleName = "Altitude Pull" };
        pullButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_ALT_PULL", simConnect, announcer);
        var inc100 = new Button { Text = "Increment 100", Location = new Point(20, 110), Size = new Size(175, 35), TabIndex = 4, AccessibleName = "Increment 100 feet" };
        inc100.Click += (s, e) => aircraft.SetAltIncrement(100, simConnect);
        var inc1000 = new Button { Text = "Increment 1000", Location = new Point(205, 110), Size = new Size(175, 35), TabIndex = 5, AccessibleName = "Increment 1000 feet" };
        inc1000.Click += (s, e) => aircraft.SetAltIncrement(1000, simConnect);
        metricButton = new Button { Location = new Point(20, 155), Size = new Size(360, 35), TabIndex = 6, AccessibleName = "Metric altitude toggle" };
        metricButton.Click += (s, e) => { aircraft.ToggleMetricAltitude(simConnect, announcer); RefreshLabel(); };
        var closeButton = new Button { Text = "Close", Location = new Point(140, 200), Size = new Size(140, 35), TabIndex = 7, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { label, altTextBox, setButton, pushButton, pullButton, inc100, inc1000, metricButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        bool metric = aircraft.MetricAlt;
        label.Text = metric ? "Altitude (30-14935 m):" : "Altitude (100-49000 ft):";
        metricButton.Text = metric ? "Metric (MTRS) — now ON (press for feet)" : "Metric (MTRS) — now OFF (press for metres)";
        metricButton.AccessibleName = metric ? "Metric altitude toggle, currently on" : "Metric altitude toggle, currently off";
    }

    protected override void SpeakInitialReadout() { aircraft.RequestFCUAltitudeWithStatus(simConnect); altTextBox.Focus(); }

    private void HandleSet()
    {
        string input = altTextBox.Text.Trim();
        if (!double.TryParse(input, out double v)) { announcer.AnnounceImmediate("Invalid number format"); altTextBox.SelectAll(); return; }
        bool metric = aircraft.MetricAlt;
        double feet = metric ? v / 0.3048 : v;
        if (feet < 100 || feet > 49000)
        {
            announcer.AnnounceImmediate(metric ? "Altitude must be between 30 and 14935 metres" : "Altitude must be between 100 and 49000 feet");
            altTextBox.SelectAll();
            return;
        }
        aircraft.SetFCUAltitudeValue(feet, simConnect, announcer);
        altTextBox.SelectAll();
    }
}
