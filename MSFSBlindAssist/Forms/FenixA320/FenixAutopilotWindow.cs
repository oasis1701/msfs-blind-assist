using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.FenixA320;

public partial class FenixAutopilotWindow : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Button ap1Button = null!;
    private Button ap2Button = null!;
    private Button athrButton = null!;
    private Button apprButton = null!;
    private Button locButton = null!;
    private Button expedButton = null!;
    private Button athrDisconnectLeftButton = null!;
    private Button athrDisconnectRightButton = null!;
    private Button apDisconnectCaptButton = null!;
    private Button apDisconnectFoButton = null!;
    private Button closeButton = null!;

    private readonly FenixA320Definition aircraft;
    private readonly SimConnect.SimConnectManager simConnect;
    private readonly ScreenReaderAnnouncer announcer;
    private IntPtr previousWindow;

    public FenixAutopilotWindow(
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
        ap1Button.Focus();
    }

    private void InitializeComponent()
    {
        // Form properties
        Text = "Fenix A320 - Autopilot Controls";
        Size = new Size(400, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        // AP1 Button
        ap1Button = new Button
        {
            Text = "AP1",
            Location = new Point(20, 20),
            Size = new Size(160, 35),
            AccessibleName = "AP1",
            AccessibleDescription = "Toggle Autopilot 1",
            TabIndex = 0
        };
        ap1Button.Click += (s, e) => HandleButtonClick("S_FCU_AP1");

        // AP2 Button
        ap2Button = new Button
        {
            Text = "AP2",
            Location = new Point(200, 20),
            Size = new Size(160, 35),
            AccessibleName = "AP2",
            AccessibleDescription = "Toggle Autopilot 2",
            TabIndex = 1
        };
        ap2Button.Click += (s, e) => HandleButtonClick("S_FCU_AP2");

        // ATHR Button
        athrButton = new Button
        {
            Text = "ATHR",
            Location = new Point(20, 70),
            Size = new Size(160, 35),
            AccessibleName = "ATHR",
            AccessibleDescription = "Toggle Auto Thrust",
            TabIndex = 2
        };
        athrButton.Click += (s, e) => HandleButtonClick("S_FCU_ATHR");

        // APPR Button
        apprButton = new Button
        {
            Text = "APPR",
            Location = new Point(200, 70),
            Size = new Size(160, 35),
            AccessibleName = "APPR",
            AccessibleDescription = "Toggle Approach mode",
            TabIndex = 3
        };
        apprButton.Click += (s, e) => HandleButtonClick("S_FCU_APPR");

        // LOC Button
        locButton = new Button
        {
            Text = "LOC",
            Location = new Point(20, 120),
            Size = new Size(160, 35),
            AccessibleName = "LOC",
            AccessibleDescription = "Toggle Localizer mode",
            TabIndex = 4
        };
        locButton.Click += (s, e) => HandleButtonClick("S_FCU_LOC");

        // EXPED Button
        expedButton = new Button
        {
            Text = "EXPED",
            Location = new Point(200, 120),
            Size = new Size(160, 35),
            AccessibleName = "EXPED",
            AccessibleDescription = "Toggle Expedite mode",
            TabIndex = 5
        };
        expedButton.Click += (s, e) => HandleButtonClick("S_FCU_EXPED");

        // A/THR Disconnect Left Button
        athrDisconnectLeftButton = new Button
        {
            Text = "A/THR Disc Left",
            Location = new Point(20, 170),
            Size = new Size(160, 35),
            AccessibleName = "A/THR Disconnect Left",
            AccessibleDescription = "Autothrottle Disconnect Left",
            TabIndex = 6
        };
        athrDisconnectLeftButton.Click += (s, e) => HandleButtonClick("S_FC_THR_INST_DISCONNECT1");

        // A/THR Disconnect Right Button
        athrDisconnectRightButton = new Button
        {
            Text = "A/THR Disc Right",
            Location = new Point(200, 170),
            Size = new Size(160, 35),
            AccessibleName = "A/THR Disconnect Right",
            AccessibleDescription = "Autothrottle Disconnect Right",
            TabIndex = 7
        };
        athrDisconnectRightButton.Click += (s, e) => HandleButtonClick("S_FC_THR_INST_DISCONNECT2");

        // AP Disconnect Captain Button
        apDisconnectCaptButton = new Button
        {
            Text = "AP Disc Capt",
            Location = new Point(20, 220),
            Size = new Size(160, 35),
            AccessibleName = "AP Disconnect Captain",
            AccessibleDescription = "Autopilot Disconnect Captain",
            TabIndex = 8
        };
        apDisconnectCaptButton.Click += (s, e) => HandleButtonClick("S_FC_CAPT_INST_DISCONNECT");

        // AP Disconnect First Officer Button
        apDisconnectFoButton = new Button
        {
            Text = "AP Disc F/O",
            Location = new Point(200, 220),
            Size = new Size(160, 35),
            AccessibleName = "AP Disconnect F/O",
            AccessibleDescription = "Autopilot Disconnect First Officer",
            TabIndex = 9
        };
        apDisconnectFoButton.Click += (s, e) => HandleButtonClick("S_FC_FO_INST_DISCONNECT");

        // Close Button
        closeButton = new Button
        {
            Text = "Close",
            Location = new Point(130, 320),
            Size = new Size(140, 35),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close window",
            TabIndex = 10
        };
        closeButton.Click += (s, e) => Close();

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            ap1Button, ap2Button, athrButton, apprButton, locButton, expedButton,
            athrDisconnectLeftButton, athrDisconnectRightButton, apDisconnectCaptButton, apDisconnectFoButton,
            closeButton
        });

        CancelButton = closeButton;
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

    private void HandleButtonClick(string varKey)
    {
        // Call the aircraft's FCU variable setter with value 1 (button press)
        aircraft.SetFCUVariable(varKey, 1, simConnect, announcer);
    }
}
