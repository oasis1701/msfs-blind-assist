using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

// A380 Baro window: enter QNH once (applies to BOTH altimeters via KOHLSMAN_SET);
// STD/QNH toggle and hPa/inHg toggle both sync both sides. Reuses the def's proven
// set routes (CAPT_QNH_SET / *_EIS_BARO_IS_STD / XMLVAR_Baro_Selector) via
// aircraft.ApplyUIVariable.
public class FBWA380BaroWindow : FBWA380FCUWindowBase
{
    private readonly TextBox qnhTextBox;
    private readonly Label unitLabel;
    private bool inHg; // entry unit; seeded from the captain side on open.

    public FBWA380BaroWindow(FlyByWireA380Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set A380 Altimeter (both sides)";
        Size = new Size(440, 250);

        inHg = (simConnect.GetCachedVariableValue("XMLVAR_Baro_Selector_HPA_1") ?? 1) < 0.5;

        unitLabel = new Label { Location = new Point(20, 25), Size = new Size(170, 20), AccessibleName = "QNH Label" };
        qnhTextBox = new TextBox { Location = new Point(195, 22), Size = new Size(90, 25), TabIndex = 0, AccessibleName = "QNH value" };
        qnhTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        var setButton = new Button { Text = "Set", Location = new Point(295, 20), Size = new Size(80, 30), TabIndex = 1, AccessibleName = "Set QNH both sides" };
        setButton.Click += (s, e) => HandleSet();
        var stdButton = new Button { Text = "STD (both)", Location = new Point(20, 65), Size = new Size(175, 35), TabIndex = 2, AccessibleName = "Standard pressure both sides" };
        stdButton.Click += (s, e) => SetStd(true);
        var qnhButton = new Button { Text = "QNH (both)", Location = new Point(205, 65), Size = new Size(175, 35), TabIndex = 3, AccessibleName = "QNH mode both sides" };
        qnhButton.Click += (s, e) => SetStd(false);
        var unitButton = new Button { Text = "hPa / inHg toggle (both)", Location = new Point(20, 110), Size = new Size(360, 35), TabIndex = 4, AccessibleName = "Pressure unit toggle both sides" };
        unitButton.Click += (s, e) => ToggleUnit();
        var closeButton = new Button { Text = "Close", Location = new Point(150, 160), Size = new Size(140, 35), TabIndex = 5, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { unitLabel, qnhTextBox, setButton, stdButton, qnhButton, unitButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;
        RefreshUnitLabel();
    }

    private void RefreshUnitLabel() { unitLabel.Text = inHg ? "QNH (26.6-32.5 inHg):" : "QNH (900-1100 hPa):"; }

    protected override void SpeakInitialReadout()
    {
        // Speak both decoded altimeters (the EFIS baro vars auto-announce on change;
        // request them so the cache is fresh, then the window's value-set confirms).
        simConnect.RequestVariable("A32NX_FCU_LEFT_EIS_BARO_HPA", forceUpdate: true);
        simConnect.RequestVariable("A32NX_FCU_RIGHT_EIS_BARO_HPA", forceUpdate: true);
        qnhTextBox.Focus();
    }

    private void HandleSet()
    {
        string input = qnhTextBox.Text.Trim();
        if (!double.TryParse(input, out double v)) { announcer.AnnounceImmediate("Invalid number format"); qnhTextBox.SelectAll(); return; }
        // CAPT_QNH_SET interprets the value in the captain side's current unit and
        // fires KOHLSMAN_SET, which moves BOTH altimeters together; it also range-
        // validates and announces. The window's entry unit (`inHg`) is always kept
        // in sync with the captain XMLVAR (seeded on open, updated by ToggleUnit),
        // so the interpretation matches what the user typed.
        aircraft.ApplyUIVariable("CAPT_QNH_SET", v, simConnect, announcer);
        qnhTextBox.SelectAll();
    }

    private void SetStd(bool std)
    {
        // Sync both EIS sides (no re-announce of the button itself).
        aircraft.ApplyUIVariable("A32NX_FCU_LEFT_EIS_BARO_IS_STD", std ? 1 : 0, simConnect, announcer);
        aircraft.ApplyUIVariable("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", std ? 1 : 0, simConnect, announcer);
        announcer.AnnounceImmediate(std ? "Standard, both sides" : "QNH, both sides");
    }

    private void ToggleUnit()
    {
        inHg = !inHg;
        int hpaUnit = inHg ? 0 : 1;
        aircraft.ApplyUIVariable("XMLVAR_Baro_Selector_HPA_1", hpaUnit, simConnect, announcer);
        aircraft.ApplyUIVariable("XMLVAR_Baro_Selector_HPA_2", hpaUnit, simConnect, announcer);
        RefreshUnitLabel();
        announcer.AnnounceImmediate(inHg ? "Inches of mercury, both sides" : "Hectopascals, both sides");
    }
}
