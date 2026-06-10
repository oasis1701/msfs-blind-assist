using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 Baro window: enter QNH once (applies to BOTH altimeters via the proven
// A32NX.FCU_EFIS_L/R_BARO_SET events, hPa*16 encoding — the same route the old
// Set-Altimeter dialog used); STD/QNH toggle fires the A32NX.FCU_EFIS_L/R_BARO_PULL
// (STD) / _BARO_PUSH (QNH) knob events — live-verified 2026-06; the old
// *_EIS_BARO_IS_STD L:var write is DEAD on the new-FCU A32NX (holds a value but
// drives nothing; stays 0 even while actually in STD — do not revert to it).
// hPa/inHg toggle = entry unit + XMLVAR_Baro_Selector both sides. Mirrors the
// A380 Baro window's shape. The captain/FO EFIS baro vars auto-announce on change.
public class FBWA320BaroWindow : FBWA320FCUWindowBase
{
    private readonly TextBox qnhTextBox;
    private readonly Label unitLabel;
    private readonly Button stdButton, qnhButton, unitButton;
    private System.Windows.Forms.Timer? _modeTimer;
    private bool inHg; // entry unit; seeded from the captain display mode on open.

    public FBWA320BaroWindow(FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set A320 Altimeter (both sides)";
        Size = new Size(440, 250);

        // A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE: 0=STD, 1=hPa, 2=inHg.
        inHg = (simConnect.GetCachedVariableValue("A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE") ?? 1) >= 1.5;

        unitLabel = new Label { Location = new Point(20, 25), Size = new Size(170, 20), AccessibleName = "QNH Label" };
        qnhTextBox = new TextBox { Location = new Point(195, 22), Size = new Size(90, 25), TabIndex = 0, AccessibleName = "QNH value" };
        qnhTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        var setButton = new Button { Text = "Set", Location = new Point(295, 20), Size = new Size(80, 30), TabIndex = 1, AccessibleName = "Set QNH both sides" };
        setButton.Click += (s, e) => HandleSet();
        stdButton = new Button { Text = "STD (both)", Location = new Point(20, 65), Size = new Size(175, 35), TabIndex = 2, AccessibleName = "Standard pressure both sides" };
        stdButton.Click += (s, e) => SetStd(true);
        qnhButton = new Button { Text = "QNH (both)", Location = new Point(205, 65), Size = new Size(175, 35), TabIndex = 3, AccessibleName = "QNH mode both sides" };
        qnhButton.Click += (s, e) => SetStd(false);
        unitButton = new Button { Text = "hPa / inHg toggle (both)", Location = new Point(20, 110), Size = new Size(360, 35), TabIndex = 4, AccessibleName = "Pressure unit toggle both sides" };
        unitButton.Click += (s, e) => ToggleUnit();
        var closeButton = new Button { Text = "Close", Location = new Point(150, 160), Size = new Size(140, 35), TabIndex = 5, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { unitLabel, qnhTextBox, setButton, stdButton, qnhButton, unitButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;
        RefreshUnitLabel();

        // Keep the STD/QNH and unit buttons showing the live state (the cockpit FCU baro
        // knobs can change it too). Mirrors the A380 baro window's "new standard".
        _modeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _modeTimer.Tick += (s, e) => RefreshModeLabels();
        _modeTimer.Start();
        RefreshModeLabels();
    }

    private void RefreshUnitLabel()
    {
        unitLabel.Text = inHg ? "QNH (26.6-32.5 inHg):" : "QNH (745-1100 hPa):";
        unitButton.Text = inHg ? "Unit — now inHg (press for hPa)" : "Unit — now hPa (press for inHg)";
        unitButton.AccessibleName = inHg ? "Pressure unit toggle, currently inches of mercury" : "Pressure unit toggle, currently hectopascals";
    }

    private void RefreshModeLabels()
    {
        // A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE: 0=STD, 1=hPa, 2=inHg. While in STD
        // the mode carries no unit information, so keep the last known entry unit.
        double mode = simConnect.GetCachedVariableValue("A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE") ?? 1;
        bool std = mode < 0.5;
        if (!std) inHg = mode >= 1.5;
        RefreshUnitLabel();
        stdButton.Text = std ? "STD (both) — ACTIVE" : "STD (both)";
        stdButton.AccessibleName = std ? "Standard pressure both sides, active" : "Standard pressure both sides";
        qnhButton.Text = std ? "QNH (both)" : "QNH (both) — ACTIVE";
        qnhButton.AccessibleName = std ? "QNH mode both sides" : "QNH mode both sides, active";
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { _modeTimer?.Stop(); _modeTimer?.Dispose(); _modeTimer = null; base.OnFormClosing(e); }

    // Fenix-style silent open (see FBWA320SpeedWindow): the forced baro reads
    // auto-announced the stale-then-fresh value on open. The EFIS baro still
    // auto-announces on a real knob change, and Set confirms the entered QNH.
    protected override void SpeakInitialReadout() { qnhTextBox.Focus(); }

    private void HandleSet()
    {
        string input = qnhTextBox.Text.Trim();
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out double v)) { announcer.AnnounceImmediate("Invalid number format"); qnhTextBox.SelectAll(); return; }
        // Interpret the typed value in the window's current entry unit and convert to
        // hectopascals; FCU_EFIS_*_BARO_SET takes hPa*16 and moves that side's drum.
        double hpa = inHg ? v * 33.8639 : v;
        if (hpa < 745 || hpa > 1100)
        {
            announcer.AnnounceImmediate(inHg ? "QNH must be between 26.6 and 32.5 inHg" : "QNH must be between 745 and 1100 hPa");
            qnhTextBox.SelectAll();
            return;
        }
        uint encoded = (uint)Math.Round(hpa * 16);
        simConnect.SendEvent("A32NX.FCU_EFIS_L_BARO_SET", encoded);
        simConnect.SendEvent("A32NX.FCU_EFIS_R_BARO_SET", encoded);
        announcer.AnnounceImmediate(inHg ? $"Altimeter set to {v:F2} inches, both sides" : $"Altimeter set to {hpa:F0} hectopascals, both sides");
        qnhTextBox.SelectAll();
    }

    private void SetStd(bool std)
    {
        // PULL = STD, PUSH = back to QNH (the FCU preserves the preselected QNH value) —
        // the same knob events the cockpit fires; reachable via TransmitClientEvent like
        // the proven A32NX.FCU_EFIS_*_BARO_SET. Live-verified round-trip 2026-06.
        string action = std ? "PULL" : "PUSH";
        simConnect.SendEvent($"A32NX.FCU_EFIS_L_BARO_{action}", 0);
        simConnect.SendEvent($"A32NX.FCU_EFIS_R_BARO_{action}", 0);
        announcer.AnnounceImmediate(std ? "Standard, both sides" : "QNH, both sides");
    }

    private void ToggleUnit()
    {
        inHg = !inHg;
        int hpaUnit = inHg ? 0 : 1;
        simConnect.ExecuteCalculatorCode($"{hpaUnit} (>L:XMLVAR_Baro_Selector_HPA_1)");
        simConnect.ExecuteCalculatorCode($"{hpaUnit} (>L:XMLVAR_Baro_Selector_HPA_2)");
        RefreshUnitLabel();
        announcer.AnnounceImmediate(inHg ? "Inches of mercury, both sides" : "Hectopascals, both sides");
    }
}
