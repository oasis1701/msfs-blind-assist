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

    private Button vsDecButton = null!;
    private Button vsIncButton = null!;
    private Button vsPushButton = null!;
    private Button vsPullButton = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private readonly IntPtr previousWindow;

    public FenixVSWindow(
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
        Text = "Fenix A320 - Vertical Speed Controls";
        Size = new Size(400, 250);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // V/S Decrease Button
        vsDecButton = new Button
        {
            Text = "V/S Decrease",
            Location = new Point(20, 20),
            Size = new Size(160, 35),
            AccessibleName = "V/S Decrease",
            AccessibleDescription = "Decrease FCU vertical speed",
            TabIndex = 0
        };
        vsDecButton.Click += (s, e) => HandleButtonClick("E_FCU_VS_DEC");

        // V/S Increase Button
        vsIncButton = new Button
        {
            Text = "V/S Increase",
            Location = new Point(200, 20),
            Size = new Size(160, 35),
            AccessibleName = "V/S Increase",
            AccessibleDescription = "Increase FCU vertical speed",
            TabIndex = 1
        };
        vsIncButton.Click += (s, e) => HandleButtonClick("E_FCU_VS_INC");

        // V/S Push Button
        vsPushButton = new Button
        {
            Text = "V/S Push",
            Location = new Point(20, 70),
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
            Location = new Point(200, 70),
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
            Location = new Point(130, 170),
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
            vsDecButton, vsIncButton, vsPushButton, vsPullButton, closeButton
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
            vsDecButton.Focus();
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
}
