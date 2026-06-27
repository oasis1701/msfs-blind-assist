using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

// A380 Baro window (Ctrl+B) — Fenix-style: Mode combo (QNH/STD, STD applies
// immediately and disables entry), Unit combo (hPa/inHg), value box, Enter = Set.
// Writes:
//   QNH value : aircraft.ApplyUIVariable("CAPT_QNH_SET", v, ...) — validates,
//               converts to mb*16, fires K:KOHLSMAN_SET (moves BOTH altimeters).
//   STD/QNH   : aircraft.ApplyUIVariable("A32NX_FCU_LEFT/RIGHT_EIS_BARO_IS_STD",...)
//               → HandleUIVariableSet fires H:A380X_EFIS_CP_BARO_{PULL|PUSH}_{1|2}
//               (PULL=STD, PUSH=QNH; MsfsBaroManager.ts). State is read back from
//               KOHLSMAN SETTING STD:1 via the re-keyed def (IS_STD L:vars removed
//               in dev FBW).
//   Unit      : aircraft.ApplyUIVariable("XMLVAR_Baro_Selector_HPA_1", ...)
//               + "_2" (dev FBW honors only _1 — upstream FIXME; set both).
public class FBWA380BaroWindow : FBWA380FCUWindowBase
{
    private readonly ComboBox modeCombo;
    private readonly ComboBox unitCombo;
    private readonly TextBox valueTextBox;
    private readonly Button setButton;
    private bool suppressUiEvents;
    private System.Windows.Forms.Timer? _modeTimer;
    // Set on every user-driven combo change. SeedFromSim skips re-seeding within
    // 1.5 s of a user change: the sim-side state lags the H-event write by several
    // frames, and the 500 ms tracking timer would otherwise snap the combo back to
    // the stale value (audibly, via the screen reader) and then forward again.
    private DateTime _lastUserChangeUtc = DateTime.MinValue;

    public FBWA380BaroWindow(FlyByWireA380Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set A380 Altimeter (both sides)";
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
        // Don't fight a fresh user selection — the sim takes a few frames to
        // reflect the H-event; re-seeding inside that window flip-flops the combo.
        if ((DateTime.UtcNow - _lastUserChangeUtc).TotalSeconds < 1.5) return;
        suppressUiEvents = true;
        try
        {
            // STD state: read from KOHLSMAN SETTING STD:1 via the re-keyed IS_STD def.
            bool std = (simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_IS_STD") ?? 0) > 0.5;
            modeCombo.SelectedIndex = std ? 1 : 0;
            // Unit: XMLVAR_Baro_Selector_HPA_1 (1=hPa, 0=inHg). Keep last unit in STD mode.
            bool inHg = (simConnect.GetCachedVariableValue("XMLVAR_Baro_Selector_HPA_1") ?? 1) < 0.5;
            if (!std)
                unitCombo.SelectedIndex = inHg ? 1 : 0;
            else if (unitCombo.SelectedIndex < 0)
                unitCombo.SelectedIndex = 0;
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
        _lastUserChangeUtc = DateTime.UtcNow;
        bool std = modeCombo.SelectedIndex == 1;
        // Route through the def's HandleUIVariableSet which fires the H-events.
        aircraft.ApplyUIVariable("A32NX_FCU_LEFT_EIS_BARO_IS_STD", std ? 1 : 0, simConnect, announcer);
        aircraft.ApplyUIVariable("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", std ? 1 : 0, simConnect, announcer);
        // No announcement: the screen reader already announces the combo change.
        UpdateControlState();
    }

    private void UnitChanged(object? sender, EventArgs e)
    {
        if (suppressUiEvents) return;
        _lastUserChangeUtc = DateTime.UtcNow;
        bool inHg = unitCombo.SelectedIndex == 1;
        // XMLVAR_Baro_Selector_HPA_1/2: 1=hPa, 0=inHg. Dev FBW honors _1; set _2 anyway.
        aircraft.ApplyUIVariable("XMLVAR_Baro_Selector_HPA_1", inHg ? 0 : 1, simConnect, announcer);
        aircraft.ApplyUIVariable("XMLVAR_Baro_Selector_HPA_2", inHg ? 0 : 1, simConnect, announcer);
        // No announcement: the screen reader already announces the combo change.
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
        if (inHg ? (v < 26.6 || v > 32.5) : (hpa < 900 || hpa > 1100))
        {
            announcer.AnnounceImmediate(inHg ? "QNH must be between 26.6 and 32.5 inches." : "QNH must be between 900 and 1100 hectopascals.");
            valueTextBox.SelectAll();
            return;
        }
        // CAPT_QNH_SET validates (in the def's own unit-aware branch), converts to mb*16,
        // fires K:KOHLSMAN_SET for both altimeters, and announces — do NOT double-announce.
        aircraft.ApplyUIVariable("CAPT_QNH_SET", v, simConnect, announcer);
        valueTextBox.SelectAll();
    }
}
