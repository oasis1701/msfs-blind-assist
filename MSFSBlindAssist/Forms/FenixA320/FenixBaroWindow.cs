using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.FenixA320;

public partial class FenixBaroWindow : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private ComboBox baroModeCombo = null!;
    private ComboBox unitCombo = null!;
    private TextBox valueTextBox = null!;
    private Button closeButton = null!;
    // A set is on its way to the aircraft (it awaits the FCU): a second Enter is refused aloud,
    // never queued, so the pilot knows to press it again once the first set ends.
    private bool _settingBaro;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private IntPtr previousWindow;

    public FenixBaroWindow(
        FenixA320Definition aircraft,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        this.aircraft = aircraft;
        this.simConnect = simConnect;
        this.announcer = announcer;

        InitializeComponent();
        SetupAccessibility();
    }

    public void ShowForm()
    {
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        baroModeCombo.Focus();
    }

    private void InitializeComponent()
    {
        Text = "Set Altimeter";
        Size = new Size(400, 220);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        // Baro Mode ComboBox (QNH / STD)
        var modeLabel = new Label
        {
            Text = "Mode:",
            Location = new Point(20, 25),
            Size = new Size(50, 20),
            AccessibleName = "Mode Label"
        };

        baroModeCombo = new ComboBox
        {
            Location = new Point(75, 22),
            Size = new Size(80, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Baro Mode",
            AccessibleDescription = "Select QNH or STD mode",
            TabIndex = 0
        };
        baroModeCombo.Items.AddRange(new object[] { "QNH", "STD" });

        // Set initial mode from current state (I_FCU_EFIS1_QNH: 0=STD, 1=QNH)
        double? qnhValue = simConnect.GetCachedVariableValue("I_FCU_EFIS1_QNH");
        baroModeCombo.SelectedIndex = (qnhValue != null && qnhValue.Value > 0.5) ? 0 : 1;

        // SelectionChangeCommitted fires only on the pilot's own pick. SelectedIndexChanged fires when the
        // combo's handle is created, which happens after the initial mode above was assigned (see
        // MainForm.PanelBuilder.cs, where the same event sent phantom writes), so opening this window
        // would step the knob: a pull to STD when the QNH light is unread and the combo defaults to STD.
        baroModeCombo.SelectionChangeCommitted += BaroModeCombo_SelectionChangeCommitted;

        // Unit ComboBox
        var unitLabel = new Label
        {
            Text = "Unit:",
            Location = new Point(170, 25),
            Size = new Size(40, 20),
            AccessibleName = "Unit Label"
        };

        unitCombo = new ComboBox
        {
            Location = new Point(215, 22),
            Size = new Size(150, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Unit",
            AccessibleDescription = "Select QNH hectopascals or inches of mercury",
            TabIndex = 1
        };
        unitCombo.Items.AddRange(new object[] { "QNH (hPa)", "Inches (inHg)" });
        unitCombo.SelectedIndex = 0;

        // Value TextBox
        var valueLabel = new Label
        {
            Text = "Value:",
            Location = new Point(20, 70),
            Size = new Size(50, 20),
            AccessibleName = "Value Label"
        };

        valueTextBox = new TextBox
        {
            Location = new Point(75, 67),
            Size = new Size(100, 25),
            AccessibleName = "Altimeter value",
            AccessibleDescription = "Enter altimeter value",
            TabIndex = 2
        };
        valueTextBox.KeyDown += ValueTextBox_KeyDown;

        // Close Button
        closeButton = new Button
        {
            Text = "Close",
            Location = new Point(130, 130),
            Size = new Size(140, 35),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close window",
            TabIndex = 4
        };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[]
        {
            modeLabel, baroModeCombo, unitLabel, unitCombo,
            valueLabel, valueTextBox, closeButton
        });

        CancelButton = closeButton;

        UpdateControlState();
    }

    private void SetupAccessibility()
    {
        KeyPreview = true;
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Close();
            }
        };

        FormClosing += (sender, e) =>
        {
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        };
    }

    private void BaroModeCombo_SelectionChangeCommitted(object? sender, EventArgs e)
    {
        bool isStd = baroModeCombo.SelectedIndex == 1;

        // Set both EFIS altimeters: a pull (+1, STD) or a push (-1, QNH) from the sim's own count. The
        // STD knob is a push/pull counter the Fenix acts on by difference, so an absolute value could
        // select the wrong mode or nothing.
        simConnect.ExecuteCalculatorCodeUnique(FenixA320Definition.CounterStepCode("S_FCU_EFIS1_BARO_STD", isStd ? 1 : -1));
        simConnect.ExecuteCalculatorCodeUnique(FenixA320Definition.CounterStepCode("S_FCU_EFIS2_BARO_STD", isStd ? 1 : -1));

        UpdateControlState();
    }

    private void UpdateControlState()
    {
        bool isStd = baroModeCombo.SelectedIndex == 1;
        unitCombo.Enabled = !isStd;
        valueTextBox.Enabled = !isStd;
    }

    // "Setting altimeter to 29.92": invariant, so a comma-decimal Windows never speaks "29,92".
    internal static string SettingAltimeterAnnouncement(double inHg)
        => FormattableString.Invariant($"Setting altimeter to {inHg:0.00}");

    private async void ValueTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            await HandleSetClick();
        }
    }

    private async System.Threading.Tasks.Task HandleSetClick()
    {
        // STD disables the value box (UpdateControlState).
        if (!valueTextBox.Enabled) return;
        // A set already on its way blocks a second, aloud: a silent drop let a corrected value vanish.
        if (_settingBaro)
        {
            announcer.AnnounceImmediate("Altimeter set in progress, press Enter again when it finishes");
            return;
        }

        string input = valueTextBox.Text.Trim();

        if (string.IsNullOrEmpty(input))
        {
            announcer.AnnounceImmediate("Please enter a value");
            valueTextBox.Focus();
            return;
        }

        bool isHpa = unitCombo.SelectedIndex == 0;

        if (isHpa)
        {
            if (!int.TryParse(input, out int hpaValue))
            {
                announcer.AnnounceImmediate("Enter a whole number for hectopascals");
                valueTextBox.Focus();
                valueTextBox.SelectAll();
                return;
            }

            if (hpaValue < 745 || hpaValue > 1100)
            {
                announcer.AnnounceImmediate("Value must be between 745 and 1100 hectopascals");
                valueTextBox.Focus();
                valueTextBox.SelectAll();
                return;
            }

            _settingBaro = true;
            try
            {
                announcer.AnnounceImmediate($"Setting altimeter to {hpaValue}");
                await aircraft.SetFCUBaro(hpaValue, null, simConnect, announcer);
            }
            catch (Exception ex)
            {
                Log.Debug("Forms", $"Error setting baro: {ex.Message}");
                announcer.AnnounceImmediate("Error setting altimeter");
            }
            finally
            {
                _settingBaro = false;
                valueTextBox.SelectAll();
            }
        }
        else
        {
            if (!Utils.NumberParsing.TryParseTyped(input, out double inHgValue))
            {
                announcer.AnnounceImmediate("Invalid number format");
                valueTextBox.Focus();
                valueTextBox.SelectAll();
                return;
            }

            if (inHgValue < 22.00 || inHgValue > 32.99)
            {
                announcer.AnnounceImmediate("Value must be between 22.00 and 32.99 inches");
                valueTextBox.Focus();
                valueTextBox.SelectAll();
                return;
            }

            _settingBaro = true;
            try
            {
                announcer.AnnounceImmediate(SettingAltimeterAnnouncement(inHgValue));
                double targetInHg = Math.Round(inHgValue, 2);
                await aircraft.SetFCUBaro(null, targetInHg, simConnect, announcer);
            }
            catch (Exception ex)
            {
                Log.Debug("Forms", $"Error setting baro: {ex.Message}");
                announcer.AnnounceImmediate("Error setting altimeter");
            }
            finally
            {
                _settingBaro = false;
                valueTextBox.SelectAll();
            }
        }
    }
}
