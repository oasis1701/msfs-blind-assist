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

    private Button spdDecButton = null!;
    private Button spdIncButton = null!;
    private Button spdPushButton = null!;
    private Button spdPullButton = null!;
    private Button spdMachButton = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private readonly IntPtr previousWindow;

    public FenixSpeedWindow(
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
        Text = "Fenix A320 - Speed Controls";
        Size = new Size(400, 310);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Speed Decrease Button
        spdDecButton = new Button
        {
            Text = "Speed Decrease",
            Location = new Point(20, 20),
            Size = new Size(160, 35),
            AccessibleName = "Speed Decrease",
            AccessibleDescription = "Decrease FCU speed",
            TabIndex = 0
        };
        spdDecButton.Click += (s, e) => HandleButtonClick("E_FCU_SPEED_DEC");

        // Speed Increase Button
        spdIncButton = new Button
        {
            Text = "Speed Increase",
            Location = new Point(200, 20),
            Size = new Size(160, 35),
            AccessibleName = "Speed Increase",
            AccessibleDescription = "Increase FCU speed",
            TabIndex = 1
        };
        spdIncButton.Click += (s, e) => HandleButtonClick("E_FCU_SPEED_INC");

        // Speed Push Button
        spdPushButton = new Button
        {
            Text = "Speed Push",
            Location = new Point(20, 70),
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
            Location = new Point(200, 70),
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
            Location = new Point(20, 120),
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
            Location = new Point(130, 230),
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
            spdDecButton, spdIncButton, spdPushButton, spdPullButton,
            spdMachButton, closeButton
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
            spdDecButton.Focus();
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
