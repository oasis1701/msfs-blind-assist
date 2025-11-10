using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.FenixA320;

public partial class FenixSpeedWindow : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Label speedLabel = null!;
    private TextBox speedTextBox = null!;
    private Button setButton = null!;
    private Button spdPushButton = null!;
    private Button spdPullButton = null!;
    private Button spdMachButton = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private IntPtr previousWindow;

    public FenixSpeedWindow(
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

    /// <summary>
    /// Shows the form and ensures it gets focus (like ChecklistForm pattern).
    /// </summary>
    public void ShowForm()
    {
        // Capture the current foreground window before showing
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false; // Flash to bring to front
        speedTextBox.Focus();
    }

    private void InitializeComponent()
    {
        // Form properties
        Text = "Set FCU Speed";
        Size = new Size(400, 310);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        // Speed Label
        speedLabel = new Label
        {
            Text = "Speed (100-399 / 0.1-0.99):",
            Location = new Point(20, 25),
            Size = new Size(160, 20),
            AccessibleName = "Speed Label"
        };

        // Speed TextBox
        speedTextBox = new TextBox
        {
            Location = new Point(190, 22),
            Size = new Size(80, 25),
            AccessibleName = "Speed value",
            AccessibleDescription = "Enter speed value: 100-399 knots or 0.10-0.99 Mach",
            TabIndex = 0
        };
        speedTextBox.KeyDown += SpeedTextBox_KeyDown;

        // Set Button
        setButton = new Button
        {
            Text = "Set",
            Location = new Point(280, 20),
            Size = new Size(80, 30),
            AccessibleName = "Set Speed",
            AccessibleDescription = "Set the entered speed value",
            TabIndex = 1
        };
        setButton.Click += async (s, e) => await HandleSetClick();

        // Speed Push Button
        spdPushButton = new Button
        {
            Text = "Speed Push",
            Location = new Point(20, 65),
            Size = new Size(160, 35),
            AccessibleName = "Speed Push",
            AccessibleDescription = "Push FCU speed knob",
            TabIndex = 2
        };
        spdPushButton.Click += (s, e) => HandleButtonClick("S_FCU_SPEED_PUSH");

        // Speed Pull Button
        spdPullButton = new Button
        {
            Text = "Speed Pull",
            Location = new Point(200, 65),
            Size = new Size(160, 35),
            AccessibleName = "Speed Pull",
            AccessibleDescription = "Pull FCU speed knob",
            TabIndex = 3
        };
        spdPullButton.Click += (s, e) => HandleButtonClick("S_FCU_SPEED_PULL");

        // SPD/MACH Button
        spdMachButton = new Button
        {
            Text = "SPD/MACH",
            Location = new Point(20, 115),
            Size = new Size(340, 35),
            AccessibleName = "SPD/MACH",
            AccessibleDescription = "Toggle speed and mach mode",
            TabIndex = 4
        };
        spdMachButton.Click += (s, e) => HandleButtonClick("S_FCU_SPD_MACH");

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
            speedLabel, speedTextBox, setButton, spdPushButton, spdPullButton,
            spdMachButton, closeButton
        });

        CancelButton = closeButton;
        AcceptButton = setButton;
    }

    private void SetupAccessibility()
    {

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

    private void SpeedTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = HandleSetClick();  // Fire and forget async call
        }
    }

    private async System.Threading.Tasks.Task HandleSetClick()
    {
        string input = speedTextBox.Text.Trim();

        if (string.IsNullOrEmpty(input))
        {
            announcer.AnnounceImmediate("Please enter a speed value");
            speedTextBox.Focus();
            return;
        }

        if (!double.TryParse(input, out double value))
        {
            announcer.AnnounceImmediate("Invalid number format");
            speedTextBox.Focus();
            speedTextBox.SelectAll();
            return;
        }

        // Accept knots (100-399) or Mach (0.10-0.99)
        if (!((value >= 100 && value <= 399) || (value >= 0.10 && value <= 0.99)))
        {
            announcer.AnnounceImmediate("Speed must be 100-399 knots or 0.10-0.99 Mach");
            speedTextBox.Focus();
            speedTextBox.SelectAll();
            return;
        }

        // Convert Mach to internal representation (multiply by 100)
        int targetSpeed = value < 1.0 ? (int)(value * 100) : (int)Math.Round(value);
        _ = aircraft.SetFCUSpeed(targetSpeed, simConnect, announcer);
        speedTextBox.SelectAll();
    }

    private void HandleButtonClick(string varKey)
    {
        // Call the aircraft's FCU variable setter with value 1 (button press)
        aircraft.SetFCUVariable(varKey, 1, simConnect, announcer);
    }
}
