using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.FenixA320;

public partial class FenixHeadingWindow : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Button hdgDecButton = null!;
    private Button hdgIncButton = null!;
    private Button hdgPushButton = null!;
    private Button hdgPullButton = null!;
    private Button hdgVsTrkFpaButton = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private readonly IntPtr previousWindow;

    public FenixHeadingWindow(
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
        Text = "Fenix A320 - Heading Controls";
        Size = new Size(400, 310);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Heading Decrease Button
        hdgDecButton = new Button
        {
            Text = "Heading Decrease",
            Location = new Point(20, 20),
            Size = new Size(160, 35),
            AccessibleName = "Heading Decrease",
            AccessibleDescription = "Decrease FCU heading",
            TabIndex = 0
        };
        hdgDecButton.Click += (s, e) => HandleButtonClick("E_FCU_HEADING_DEC");

        // Heading Increase Button
        hdgIncButton = new Button
        {
            Text = "Heading Increase",
            Location = new Point(200, 20),
            Size = new Size(160, 35),
            AccessibleName = "Heading Increase",
            AccessibleDescription = "Increase FCU heading",
            TabIndex = 1
        };
        hdgIncButton.Click += (s, e) => HandleButtonClick("E_FCU_HEADING_INC");

        // Heading Push Button
        hdgPushButton = new Button
        {
            Text = "Heading Push",
            Location = new Point(20, 70),
            Size = new Size(160, 35),
            AccessibleName = "Heading Push",
            AccessibleDescription = "Push FCU heading knob",
            TabIndex = 2
        };
        hdgPushButton.Click += (s, e) => HandleButtonClick("S_FCU_HEADING_PUSH");

        // Heading Pull Button
        hdgPullButton = new Button
        {
            Text = "Heading Pull",
            Location = new Point(200, 70),
            Size = new Size(160, 35),
            AccessibleName = "Heading Pull",
            AccessibleDescription = "Pull FCU heading knob",
            TabIndex = 3
        };
        hdgPullButton.Click += (s, e) => HandleButtonClick("S_FCU_HEADING_PULL");

        // HDG/VS TRK/FPA Button
        hdgVsTrkFpaButton = new Button
        {
            Text = "HDG/VS TRK/FPA",
            Location = new Point(20, 120),
            Size = new Size(340, 35),
            AccessibleName = "HDG/VS TRK/FPA",
            AccessibleDescription = "Toggle HDG/VS and TRK/FPA mode",
            TabIndex = 4
        };
        hdgVsTrkFpaButton.Click += (s, e) => HandleButtonClick("S_FCU_HDGVS_TRKFPA");

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
            hdgDecButton, hdgIncButton, hdgPushButton, hdgPullButton,
            hdgVsTrkFpaButton, closeButton
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
            hdgDecButton.Focus();
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
