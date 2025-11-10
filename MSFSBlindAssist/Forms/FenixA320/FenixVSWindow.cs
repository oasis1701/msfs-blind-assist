using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.FenixA320;

public partial class FenixVSWindow : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Label vsLabel = null!;
    private TextBox vsTextBox = null!;
    private Button setButton = null!;
    private Button vsPushButton = null!;
    private Button vsPullButton = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private IntPtr previousWindow;

    public FenixVSWindow(
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
        vsTextBox.Focus();
    }

    private void InitializeComponent()
    {
        // Form properties
        Text = "Fenix A320 - Vertical Speed Controls";
        Size = new Size(400, 250);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        // V/S Label
        vsLabel = new Label
        {
            Text = "V/S (-6000 to 6000):",
            Location = new Point(20, 25),
            Size = new Size(140, 20),
            AccessibleName = "Vertical Speed Label"
        };

        // V/S TextBox
        vsTextBox = new TextBox
        {
            Location = new Point(170, 22),
            Size = new Size(90, 25),
            AccessibleName = "Vertical speed value",
            AccessibleDescription = "Enter vertical speed value between -6000 and 6000 feet per minute",
            TabIndex = 0
        };
        vsTextBox.KeyDown += VsTextBox_KeyDown;

        // Set Button
        setButton = new Button
        {
            Text = "Set",
            Location = new Point(270, 20),
            Size = new Size(90, 30),
            AccessibleName = "Set Vertical Speed",
            AccessibleDescription = "Set the entered vertical speed value",
            TabIndex = 1
        };
        setButton.Click += async (s, e) => await HandleSetClick();

        // V/S Push Button
        vsPushButton = new Button
        {
            Text = "V/S Push",
            Location = new Point(20, 65),
            Size = new Size(160, 35),
            AccessibleName = "V/S Push",
            AccessibleDescription = "Push FCU vertical speed knob",
            TabIndex = 2
        };
        vsPushButton.Click += (s, e) => HandleButtonClick("S_FCU_VERTICAL_SPEED_PUSH");

        // V/S Pull Button
        vsPullButton = new Button
        {
            Text = "V/S Pull",
            Location = new Point(200, 65),
            Size = new Size(160, 35),
            AccessibleName = "V/S Pull",
            AccessibleDescription = "Pull FCU vertical speed knob",
            TabIndex = 3
        };
        vsPullButton.Click += (s, e) => HandleButtonClick("S_FCU_VERTICAL_SPEED_PULL");

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
            vsLabel, vsTextBox, setButton, vsPushButton, vsPullButton, closeButton
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

    private void VsTextBox_KeyDown(object? sender, KeyEventArgs e)
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
        string input = vsTextBox.Text.Trim();

        if (string.IsNullOrEmpty(input))
        {
            announcer.AnnounceImmediate("Please enter a vertical speed value");
            vsTextBox.Focus();
            return;
        }

        if (!double.TryParse(input, out double value))
        {
            announcer.AnnounceImmediate("Invalid number format");
            vsTextBox.Focus();
            vsTextBox.SelectAll();
            return;
        }

        if (value < -6000 || value > 6000)
        {
            announcer.AnnounceImmediate("Vertical speed must be between -6000 and 6000 feet per minute");
            vsTextBox.Focus();
            vsTextBox.SelectAll();
            return;
        }

        int targetVS = (int)Math.Round(value);
        _ = aircraft.SetFCUVS(targetVS, simConnect, announcer);
        vsTextBox.SelectAll();
    }

    private void HandleButtonClick(string varKey)
    {
        // Call the aircraft's FCU variable setter with value 1 (button press)
        aircraft.SetFCUVariable(varKey, 1, simConnect, announcer);
    }
}
