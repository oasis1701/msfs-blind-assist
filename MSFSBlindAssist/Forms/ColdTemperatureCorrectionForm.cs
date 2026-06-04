using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Accessible cold-temperature altitude-correction calculator.
///
/// In very cold air the atmosphere is denser than ISA, so the pressure altimeter
/// OVER-reads — the aircraft is actually LOWER than indicated. On a cold day the
/// published minimum altitudes on an approach plate must be corrected UPWARD to
/// preserve real obstacle clearance. This is the same calculator the FlyByWire
/// EFB exposes on its Performance page (TemperatureCorrectionWidget), but that
/// widget is a canvas/SimpleInput page a screen reader can't operate, so we
/// re-implement the math here as a plain accessible dialog.
///
/// Math: the EUROCONTROL "Doc 2940 / cold-temperature correction" workbook
/// formula (identical to the FBW source). Pure offline arithmetic — no SimVars,
/// no Coherent, no aircraft required — so it works on the ground, in the planning
/// phase, on any aircraft.
/// </summary>
public partial class ColdTemperatureCorrectionForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TextBox elevationTextBox = null!;
    private TextBox temperatureTextBox = null!;
    private TextBox altitudesTextBox = null!;
    private TextBox resultsTextBox = null!;
    private Button calculateButton = null!;
    private Button closeButton = null!;
    private Label elevationLabel = null!;
    private Label temperatureLabel = null!;
    private Label altitudesLabel = null!;
    private Label resultsLabel = null!;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr previousWindow;

    public ColdTemperatureCorrectionForm(ScreenReaderAnnouncer announcer)
    {
        previousWindow = GetForegroundWindow();
        _announcer = announcer;
        InitializeComponent();
        SetupAccessibility();
    }

    public void ShowForm() => Show();

    /// <summary>
    /// EUROCONTROL cold-temperature altitude correction. Returns the corrected
    /// (raised) altitude in feet, rounded UP to the nearest 10 ft. When the
    /// temperature is warm enough that the correction is zero or negative, the
    /// published altitude is returned unchanged (you never correct downward).
    /// Identical to FBW's TemperatureCorrectionWidget.calculateCorrectedAltitude.
    /// </summary>
    public static double CorrectedAltitude(double publishedAlt, double fieldElevation, double temperatureC)
    {
        double tAtField = temperatureC + 0.00198 * fieldElevation;
        double correction = (publishedAlt - fieldElevation) *
            ((15 - tAtField) /
             (273 + tAtField - 0.5 * 0.00198 * (publishedAlt - fieldElevation + fieldElevation)));

        if (correction <= 0)
            return publishedAlt;

        // Round UP to the nearest 10 ft (MathUtils.ceil(value, 10) in the source).
        return Math.Ceiling((publishedAlt + correction) / 10.0) * 10.0;
    }

    private void InitializeComponent()
    {
        Text = "Cold Temperature Altitude Correction";
        Size = new Size(520, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        elevationLabel = new Label
        {
            Text = "Aerodrome (field) elevation, feet:",
            Location = new Point(20, 20),
            Size = new Size(300, 20),
            AccessibleName = "Aerodrome field elevation label"
        };
        elevationTextBox = new TextBox
        {
            Location = new Point(330, 18),
            Size = new Size(150, 25),
            AccessibleName = "Aerodrome field elevation in feet",
            AccessibleDescription = "Elevation of the destination airport in feet. Negative values are allowed."
        };
        elevationTextBox.KeyDown += AnyField_KeyDown;

        temperatureLabel = new Label
        {
            Text = "Reported temperature, Celsius:",
            Location = new Point(20, 55),
            Size = new Size(300, 20),
            AccessibleName = "Reported temperature label"
        };
        temperatureTextBox = new TextBox
        {
            Location = new Point(330, 53),
            Size = new Size(150, 25),
            AccessibleName = "Reported aerodrome temperature in Celsius",
            AccessibleDescription = "The reported surface temperature at the aerodrome in degrees Celsius. Negative values are allowed."
        };
        temperatureTextBox.KeyDown += AnyField_KeyDown;

        altitudesLabel = new Label
        {
            Text = "Published altitudes (feet), one per line:",
            Location = new Point(20, 90),
            Size = new Size(460, 20),
            AccessibleName = "Published altitudes label"
        };
        altitudesTextBox = new TextBox
        {
            Location = new Point(20, 115),
            Size = new Size(460, 120),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            WordWrap = false,
            Font = new Font("Consolas", 10, FontStyle.Regular),
            AccessibleName = "Published altitudes, one per line",
            AccessibleDescription = "Enter each procedure or minimum altitude from the approach plate on its own line, in feet. Then press the Calculate button."
        };

        calculateButton = new Button
        {
            Text = "C&alculate",
            Location = new Point(20, 245),
            Size = new Size(120, 30),
            AccessibleName = "Calculate corrected altitudes",
            AccessibleDescription = "Compute the cold-temperature corrected altitudes for every line entered above."
        };
        calculateButton.Click += (_, _) => Calculate();

        resultsLabel = new Label
        {
            Text = "Corrected altitudes:",
            Location = new Point(20, 290),
            Size = new Size(460, 20),
            AccessibleName = "Corrected altitudes label"
        };
        resultsTextBox = new TextBox
        {
            Location = new Point(20, 315),
            Size = new Size(460, 120),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10, FontStyle.Regular),
            AccessibleName = "Corrected altitudes results",
            AccessibleDescription = "The cold-temperature corrected altitudes. Read with the arrow keys.",
            Text = "Enter field elevation, temperature and one or more published altitudes, then press Calculate."
        };

        closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(405, 445),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close the cold temperature correction window"
        };
        closeButton.Click += CloseButton_Click;

        Controls.AddRange(new Control[]
        {
            elevationLabel, elevationTextBox,
            temperatureLabel, temperatureTextBox,
            altitudesLabel, altitudesTextBox,
            calculateButton,
            resultsLabel, resultsTextBox,
            closeButton
        });

        CancelButton = closeButton;
    }

    private void SetupAccessibility()
    {
        elevationTextBox.TabIndex = 0;
        temperatureTextBox.TabIndex = 1;
        altitudesTextBox.TabIndex = 2;
        calculateButton.TabIndex = 3;
        resultsTextBox.TabIndex = 4;
        closeButton.TabIndex = 5;

        Load += (_, _) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;
            elevationTextBox.Focus();
        };
    }

    // Enter in the elevation/temperature single-line fields runs the calculation
    // (the altitudes box accepts Return for new lines, so it is NOT wired here).
    private void AnyField_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Calculate();
        }
    }

    private void Calculate()
    {
        if (!TryParseSigned(elevationTextBox.Text, out double fieldElevation))
        {
            ShowError("Enter a valid field elevation in feet.");
            elevationTextBox.Focus();
            return;
        }
        if (!TryParseSigned(temperatureTextBox.Text, out double temperatureC))
        {
            ShowError("Enter a valid temperature in degrees Celsius.");
            temperatureTextBox.Focus();
            return;
        }

        var lines = altitudesTextBox.Text
            .Replace("\r", "")
            .Split('\n');

        var outputs = new List<string>();
        int valid = 0;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (!TryParseSigned(line, out double publishedAlt))
            {
                outputs.Add($"{line}: not a number");
                continue;
            }
            double corrected = CorrectedAltitude(publishedAlt, fieldElevation, temperatureC);
            int add = (int)Math.Round(corrected - publishedAlt);
            string pub = $"{publishedAlt:0}";
            string cor = $"{corrected:0}";
            outputs.Add(add <= 0
                ? $"Published {pub} ft: no correction needed (warm), use {cor} ft"
                : $"Published {pub} ft: corrected {cor} ft (add {add} ft)");
            valid++;
        }

        if (valid == 0)
        {
            ShowError("Enter at least one published altitude, one per line.");
            altitudesTextBox.Focus();
            return;
        }

        string header = $"Cold temperature correction at {fieldElevation:0} ft field elevation, {temperatureC:0.#} Celsius:";
        resultsTextBox.Text = header + Environment.NewLine + string.Join(Environment.NewLine, outputs);

        // For a SINGLE altitude, speak the answer directly (instant feedback). For several,
        // say nothing extra — just move focus into the results box (below) so the screen reader
        // lands there and the user arrows through the lines. (No "N computed" chatter — per user.)
        if (outputs.Count == 1)
            _announcer.AnnounceImmediate(outputs[0]);

        // Move focus to the results field, caret at the top, so the user reads from line 1.
        resultsTextBox.Focus();
        resultsTextBox.SelectionStart = 0;
        resultsTextBox.SelectionLength = 0;
    }

    private void ShowError(string message)
    {
        resultsTextBox.Text = message;
        _announcer.AnnounceImmediate(message);
    }

    // Accept a signed integer/decimal; reject blanks and garbage. Invariant
    // culture so a comma isn't mistaken for a decimal point.
    private static bool TryParseSigned(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return double.TryParse(text.Trim(),
            System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private void CloseButton_Click(object? sender, EventArgs e)
    {
        Close();
        if (previousWindow != IntPtr.Zero)
            SetForegroundWindow(previousWindow);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (previousWindow != IntPtr.Zero)
            SetForegroundWindow(previousWindow);
    }
}
