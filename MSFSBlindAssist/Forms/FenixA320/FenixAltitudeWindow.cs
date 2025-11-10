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

    private Label altitudeLabel = null!;
    private TextBox altitudeTextBox = null!;
    private Button setButton = null!;
    private Button altPushButton = null!;
    private Button altPullButton = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private IntPtr previousWindow;

    public FenixAltitudeWindow(
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
        altitudeTextBox.Focus();
    }

    private void InitializeComponent()
    {
        // Form properties
        Text = "Set FCU altitude";
        Size = new Size(400, 250);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        // Altitude Label
        altitudeLabel = new Label
        {
            Text = "Altitude (100-49000):",
            Location = new Point(20, 25),
            Size = new Size(130, 20),
            AccessibleName = "Altitude Label"
        };

        // Altitude TextBox
        altitudeTextBox = new TextBox
        {
            Location = new Point(160, 22),
            Size = new Size(100, 25),
            AccessibleName = "Altitude value",
            AccessibleDescription = "Enter altitude value between 100 and 49000 feet",
            TabIndex = 0
        };
        altitudeTextBox.KeyDown += AltitudeTextBox_KeyDown;

        // Set Button
        setButton = new Button
        {
            Text = "Set",
            Location = new Point(270, 20),
            Size = new Size(90, 30),
            AccessibleName = "Set Altitude",
            AccessibleDescription = "Set the entered altitude value",
            TabIndex = 1
        };
        setButton.Click += async (s, e) => await HandleSetClick();

        // Altitude Push Button
        altPushButton = new Button
        {
            Text = "Altitude Push",
            Location = new Point(20, 65),
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
            Location = new Point(200, 65),
            Size = new Size(160, 35),
            AccessibleName = "Altitude Pull",
            AccessibleDescription = "Pull FCU altitude knob",
            TabIndex = 3
        };
        altPullButton.Click += (s, e) => HandleButtonClick("S_FCU_ALTITUDE_PULL");

        // Close Button
        closeButton = new Button
        {
            Text = "Close",
            Location = new Point(130, 160),
            Size = new Size(140, 35),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close window",
            TabIndex = 4
        };
        closeButton.Click += (s, e) => Close();

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            altitudeLabel, altitudeTextBox, setButton, altPushButton, altPullButton,
            closeButton
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

    private void AltitudeTextBox_KeyDown(object? sender, KeyEventArgs e)
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
        string input = altitudeTextBox.Text.Trim();

        if (string.IsNullOrEmpty(input))
        {
            announcer.AnnounceImmediate("Please enter an altitude value");
            altitudeTextBox.Focus();
            return;
        }

        if (!double.TryParse(input, out double value))
        {
            announcer.AnnounceImmediate("Invalid number format");
            altitudeTextBox.Focus();
            altitudeTextBox.SelectAll();
            return;
        }

        if (value < 100 || value > 49000)
        {
            announcer.AnnounceImmediate("Altitude must be between 100 and 49000 feet");
            altitudeTextBox.Focus();
            altitudeTextBox.SelectAll();
            return;
        }

        int targetAltitude = (int)Math.Round(value);
        // Always use 100ft mode (userPreferredMode = 0), altitude scale is now in panels only
        _ = aircraft.SetFCUAltitude(targetAltitude, simConnect, announcer, 0);
        altitudeTextBox.SelectAll();
    }

    private void HandleButtonClick(string varKey)
    {
        // Call the aircraft's FCU variable setter with value 1 (button press)
        aircraft.SetFCUVariable(varKey, 1, simConnect, announcer);
    }
}
