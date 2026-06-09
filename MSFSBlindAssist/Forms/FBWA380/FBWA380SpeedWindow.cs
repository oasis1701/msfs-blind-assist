using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

// A380 FCU Speed window: enter 100-399 kt or 0.10-0.99 Mach; Push/Pull; SPD/MACH.
public class FBWA380SpeedWindow : FBWA380FCUWindowBase
{
    private readonly TextBox speedTextBox;
    private readonly Button machButton;
    private System.Windows.Forms.Timer? _modeTimer;

    public FBWA380SpeedWindow(FlyByWireA380Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set FCU Speed";
        Size = new Size(400, 230);

        var label = new Label { Text = "Speed (100-399 / 0.10-0.99):", Location = new Point(20, 25), Size = new Size(170, 20), AccessibleName = "Speed Label" };
        speedTextBox = new TextBox { Location = new Point(195, 22), Size = new Size(75, 25), TabIndex = 0, AccessibleName = "Speed value", AccessibleDescription = "Enter 100-399 knots or 0.10-0.99 Mach" };
        speedTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        var setButton = new Button { Text = "Set", Location = new Point(280, 20), Size = new Size(80, 30), TabIndex = 1, AccessibleName = "Set Speed" };
        setButton.Click += (s, e) => HandleSet();
        var pushButton = new Button { Text = "Speed Push (managed)", Location = new Point(20, 65), Size = new Size(165, 35), TabIndex = 2, AccessibleName = "Speed Push" };
        pushButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_SPD_PUSH", simConnect, announcer, readback: false);
        var pullButton = new Button { Text = "Speed Pull (selected)", Location = new Point(195, 65), Size = new Size(165, 35), TabIndex = 3, AccessibleName = "Speed Pull" };
        pullButton.Click += (s, e) => aircraft.FireFCUButton("A32NX.FCU_SPD_PULL", simConnect, announcer, readback: false);
        machButton = new Button { Text = "SPD / MACH toggle", Location = new Point(20, 110), Size = new Size(340, 35), TabIndex = 4, AccessibleName = "Speed Mach toggle" };
        machButton.Click += (s, e) => { aircraft.FireFCUButton("A32NX.FCU_SPD_MACH_TOGGLE_PUSH", simConnect, announcer, readback: false); UpdateMachLabel(); };
        var closeButton = new Button { Text = "Close", Location = new Point(130, 155), Size = new Size(140, 35), TabIndex = 5, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { label, speedTextBox, setButton, pushButton, pullButton, machButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;

        // Reflect the live SPD/MACH mode in the toggle button's label so the user
        // always knows which mode they're in (and which one a press switches to).
        // Derived from the cached selected-speed: a Mach target reads < 1, a knots
        // target reads >= 100, and -1 (managed/dashes) is left ambiguous.
        _modeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _modeTimer.Tick += (s, e) => UpdateMachLabel();
    }

    // Fenix-style: open SILENT. The old RequestFCUSpeedWithStatus forced a SimConnect
    // read that announced the STALE cached value first, then the fresh one (the "previous
    // state, then the right value" double the user heard). The value is confirmed on Set;
    // managed/selected changes auto-announce via the always-on monitor.
    protected override void SpeakInitialReadout() { UpdateMachLabel(); _modeTimer?.Start(); speedTextBox.Focus(); }

    private void UpdateMachLabel()
    {
        double v = simConnect.GetCachedVariableValue("A32NX_AUTOPILOT_SPEED_SELECTED") ?? -1;
        string text, mode;
        if (v > 0 && v < 1) { mode = "Mach"; text = "SPD / MACH toggle — now Mach (press for knots)"; }
        else if (v >= 100) { mode = "knots"; text = "SPD / MACH toggle — now knots (press for Mach)"; }
        else { mode = "managed"; text = "SPD / MACH toggle"; }
        if (machButton.Text != text)
        {
            machButton.Text = text;
            machButton.AccessibleName = $"Speed Mach toggle, currently {mode}";
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { _modeTimer?.Stop(); _modeTimer?.Dispose(); _modeTimer = null; base.OnFormClosing(e); }

    private void HandleSet()
    {
        string input = speedTextBox.Text.Trim();
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out double v)) { announcer.AnnounceImmediate("Invalid number format"); speedTextBox.SelectAll(); return; }
        if (!((v >= 100 && v <= 399) || (v >= 0.10 && v <= 0.99))) { announcer.AnnounceImmediate("Speed must be 100-399 knots or 0.10-0.99 Mach"); speedTextBox.SelectAll(); return; }
        int internalSpeed = v < 1.0 ? (int)Math.Round(v * 100) : (int)Math.Round(v);
        aircraft.SetFCUSpeedValue(internalSpeed, simConnect, announcer);
        speedTextBox.SelectAll();
    }
}
