using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.FenixA320;

public partial class FenixAltitudeWindow : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Button altDecButton = null!;
    private Button altIncButton = null!;
    private Button altPushButton = null!;
    private Button altPullButton = null!;
    private ComboBox altScaleComboBox = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private readonly IntPtr previousWindow;

    public FenixAltitudeWindow(
        FenixA320Definition aircraft,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();

        this.aircraft = aircraft;
        this.simConnect = simConnect;
        this.announcer = announcer;

        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        // Form properties
        Text = "Fenix A320 - Altitude Controls";
        Size = new Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Altitude Decrease Button
        altDecButton = new Button
        {
            Text = "Altitude Decrease",
            Location = new Point(20, 20),
            Size = new Size(160, 35),
            AccessibleName = "Altitude Decrease",
            AccessibleDescription = "Decrease FCU altitude",
            TabIndex = 0
        };
        altDecButton.Click += (s, e) => HandleButtonClick("E_FCU_ALTITUDE_DEC");

        // Altitude Increase Button
        altIncButton = new Button
        {
            Text = "Altitude Increase",
            Location = new Point(200, 20),
            Size = new Size(160, 35),
            AccessibleName = "Altitude Increase",
            AccessibleDescription = "Increase FCU altitude",
            TabIndex = 1
        };
        altIncButton.Click += (s, e) => HandleButtonClick("E_FCU_ALTITUDE_INC");

        // Altitude Push Button
        altPushButton = new Button
        {
            Text = "Altitude Push",
            Location = new Point(20, 70),
            Size = new Size(160, 35),
            AccessibleName = "Altitude Push",
            AccessibleDescription = "Push FCU altitude knob",
            TabIndex = 2
        };
        altPushButton.Click += (s, e) => HandleButtonClick("S_FCU_ALTITUDE_PUSH");

        // Altitude Pull Button
        altPullButton = new Button
        {
            Text = "Altitude Pull",
            Location = new Point(200, 70),
            Size = new Size(160, 35),
            AccessibleName = "Altitude Pull",
            AccessibleDescription = "Pull FCU altitude knob",
            TabIndex = 3
        };
        altPullButton.Click += (s, e) => HandleButtonClick("S_FCU_ALTITUDE_PULL");

        // Altitude Scale ComboBox
        var scaleLabel = new Label
        {
            Text = "Altitude Scale:",
            Location = new Point(20, 125),
            Size = new Size(100, 20),
            AccessibleName = "Altitude Scale Label"
        };

        altScaleComboBox = new ComboBox
        {
            Location = new Point(130, 122),
            Size = new Size(230, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Altitude Scale",
            AccessibleDescription = "Select altitude increment scale",
            TabIndex = 4
        };
        altScaleComboBox.Items.AddRange(new object[] { "100 feet", "1000 feet" });
        altScaleComboBox.SelectedIndex = 0;
        altScaleComboBox.SelectedIndexChanged += AltScaleComboBox_SelectedIndexChanged;

        // Close Button
        closeButton = new Button
        {
            Text = "Close",
            Location = new Point(130, 220),
            Size = new Size(140, 35),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close window",
            TabIndex = 5
        };
        closeButton.Click += (s, e) => Close();

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            altDecButton, altIncButton, altPushButton, altPullButton,
            scaleLabel, altScaleComboBox, closeButton
        });

        CancelButton = closeButton;
    }

    private void SetupAccessibility()
    {
        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            altDecButton.Focus();
        };

        // Handle escape key and form closing
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
            // Restore focus to the previous window (likely the simulator)
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        };
    }

    private void HandleButtonClick(string varKey)
    {
        // Call the aircraft's FCU variable setter with value 1 (button press)
        aircraft.SetFCUVariable(varKey, 1, simConnect, announcer);
    }

    private void AltScaleComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // ComboBox: 0 = 100ft, 1 = 1000ft
        aircraft.SetFCUVariable("S_FCU_ALTITUDE_SCALE", altScaleComboBox.SelectedIndex, simConnect, announcer);
    }
}
