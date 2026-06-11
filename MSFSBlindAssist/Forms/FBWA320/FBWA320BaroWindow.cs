using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 Baro window (Ctrl+B) — Fenix-style: Mode combo (QNH/STD, STD applies
// immediately and disables entry), Unit combo (hPa/inHg), value box, Enter = Set.
// Writes: A32NX.FCU_EFIS_L/R_BARO_SET (hPa*16) for the value; _BARO_PULL (STD) /
// _BARO_PUSH (QNH) knob events; unit = A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG via the
// calc path (the dev-FCU live input — XMLVAR_Baro_Selector_HPA_* was removed from
// the A32NX and is DEAD; do not revert to it). STD state reads
// A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE (0=STD/1=hPa/2=inHg).
public class FBWA320BaroWindow : FBWA320FCUWindowBase
{
    private readonly ComboBox modeCombo;
    private readonly ComboBox unitCombo;
    private readonly TextBox valueTextBox;
    private readonly Button setButton;
    private bool suppressUiEvents;
    private System.Windows.Forms.Timer? _modeTimer;

    public FBWA320BaroWindow(FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set Altimeter (both sides)";
        Size = new Size(420, 220);

        var modeLabel = new Label { Text = "Mode:", Location = new Point(20, 25), Size = new Size(50, 20) };
        modeCombo = new ComboBox
        {
            Location = new Point(75, 22), Size = new Size(80, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 0,
            AccessibleName = "Baro Mode", AccessibleDescription = "Select QNH or STD mode, applies to both sides"
        };
        modeCombo.Items.AddRange(new object[] { "QNH", "STD" });

        var unitLabel = new Label { Text = "Unit:", Location = new Point(170, 25), Size = new Size(40, 20) };
        unitCombo = new ComboBox
        {
            Location = new Point(215, 22), Size = new Size(160, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 1,
            AccessibleName = "Unit", AccessibleDescription = "Hectopascals or inches of mercury, both sides"
        };
        unitCombo.Items.AddRange(new object[] { "QNH (hPa)", "Inches (inHg)" });

        var valueLabel = new Label { Text = "Value:", Location = new Point(20, 70), Size = new Size(50, 20) };
        valueTextBox = new TextBox
        {
            Location = new Point(75, 67), Size = new Size(100, 25), TabIndex = 2,
            AccessibleName = "Altimeter value", AccessibleDescription = "Enter altimeter value, Enter to set"
        };
        valueTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        setButton = new Button { Text = "Set", Location = new Point(190, 65), Size = new Size(90, 30), TabIndex = 3, AccessibleName = "Set Altimeter" };
        setButton.Click += (s, e) => HandleSet();
        var closeButton = new Button { Text = "Close", Location = new Point(130, 130), Size = new Size(140, 35), TabIndex = 4, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { modeLabel, modeCombo, unitLabel, unitCombo, valueLabel, valueTextBox, setButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;

        SeedFromSim();
        modeCombo.SelectedIndexChanged += ModeChanged;
        unitCombo.SelectedIndexChanged += UnitChanged;

        // Track cockpit-side changes (FCU knob) while the window is open.
        _modeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _modeTimer.Tick += (s, e) => SeedFromSim();
        _modeTimer.Start();
    }

    private void SeedFromSim()
    {
        suppressUiEvents = true;
        try
        {
            // 0=STD, 1=hPa, 2=inHg; while STD the mode carries no unit info — keep the last unit.
            double mode = simConnect.GetCachedVariableValue("A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE") ?? 1;
            bool std = mode < 0.5;
            modeCombo.SelectedIndex = std ? 1 : 0;
            if (!std) unitCombo.SelectedIndex = mode >= 1.5 ? 1 : 0;
            else if (unitCombo.SelectedIndex < 0) unitCombo.SelectedIndex = 0;
            UpdateControlState();
        }
        finally { suppressUiEvents = false; }
    }

    private void UpdateControlState()
    {
        bool std = modeCombo.SelectedIndex == 1;
        unitCombo.Enabled = !std;
        valueTextBox.Enabled = !std;
        setButton.Enabled = !std;
    }

    private void ModeChanged(object? sender, EventArgs e)
    {
        if (suppressUiEvents) return;
        bool std = modeCombo.SelectedIndex == 1;
        // PULL = STD, PUSH = QNH — the cockpit knob events, both sides.
        string action = std ? "PULL" : "PUSH";
        simConnect.SendEvent($"A32NX.FCU_EFIS_L_BARO_{action}", 0);
        simConnect.SendEvent($"A32NX.FCU_EFIS_R_BARO_{action}", 0);
        announcer.AnnounceImmediate(std ? "Standard, both sides" : "QNH, both sides");
        UpdateControlState();
    }

    private void UnitChanged(object? sender, EventArgs e)
    {
        if (suppressUiEvents) return;
        bool inHg = unitCombo.SelectedIndex == 1;
        // Dev-FCU live unit input (read every frame by the FCU model).
        simConnect.ExecuteCalculatorCode($"{(inHg ? 1 : 0)} (>L:A32NX_FCU_EFIS_L_BARO_IS_INHG)");
        simConnect.ExecuteCalculatorCode($"{(inHg ? 1 : 0)} (>L:A32NX_FCU_EFIS_R_BARO_IS_INHG)");
        announcer.AnnounceImmediate(inHg ? "Inches of mercury, both sides" : "Hectopascals, both sides");
    }

    protected override void SpeakInitialReadout() { valueTextBox.Focus(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    { _modeTimer?.Stop(); _modeTimer?.Dispose(); _modeTimer = null; base.OnFormClosing(e); }

    private void HandleSet()
    {
        if (!setButton.Enabled) return;
        string input = valueTextBox.Text.Trim();
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
        { announcer.AnnounceImmediate("Invalid number format"); valueTextBox.SelectAll(); return; }
        bool inHg = unitCombo.SelectedIndex == 1;
        double hpa = inHg ? v * 33.8639 : v;
        if (inHg ? (v < 22.00 || v > 32.99) : (hpa < 745 || hpa > 1100))
        {
            announcer.AnnounceImmediate(inHg ? "Value must be between 22.00 and 32.99 inches" : "Value must be between 745 and 1100 hectopascals");
            valueTextBox.SelectAll();
            return;
        }
        uint encoded = (uint)Math.Round(hpa * 16);
        simConnect.SendEvent("A32NX.FCU_EFIS_L_BARO_SET", encoded);
        simConnect.SendEvent("A32NX.FCU_EFIS_R_BARO_SET", encoded);
        announcer.AnnounceImmediate(inHg ? $"Altimeter set to {v:F2} inches, both sides" : $"Altimeter set to {hpa:F0} hectopascals, both sides");
        valueTextBox.SelectAll();
    }
}
