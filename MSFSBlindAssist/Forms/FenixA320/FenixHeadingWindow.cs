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

    private Label headingLabel = null!;
    private TextBox headingTextBox = null!;
    private Button setButton = null!;
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

        // Heading Label
        headingLabel = new Label
        {
            Text = "Heading (0-360):",
            Location = new Point(20, 25),
            Size = new Size(120, 20),
            AccessibleName = "Heading Label"
        };

        // Heading TextBox
        headingTextBox = new TextBox
        {
            Location = new Point(150, 22),
            Size = new Size(100, 25),
            AccessibleName = "Heading value",
            AccessibleDescription = "Enter heading value between 0 and 360 degrees",
            TabIndex = 0
        };
        headingTextBox.KeyDown += HeadingTextBox_KeyDown;

        // Set Button
        setButton = new Button
        {
            Text = "Set",
            Location = new Point(260, 20),
            Size = new Size(100, 30),
            AccessibleName = "Set Heading",
            AccessibleDescription = "Set the entered heading value",
            TabIndex = 1
        };
        setButton.Click += async (s, e) => await HandleSetClick();

        // Heading Push Button
        hdgPushButton = new Button
        {
            Text = "Heading Push",
            Location = new Point(20, 65),
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
            Location = new Point(200, 65),
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
            Location = new Point(20, 115),
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
            headingLabel, headingTextBox, setButton, hdgPushButton, hdgPullButton,
            hdgVsTrkFpaButton, closeButton
        });

        CancelButton = closeButton;
        AcceptButton = setButton;
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
            headingTextBox.Focus();
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

    private void HeadingTextBox_KeyDown(object? sender, KeyEventArgs e)
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
        string input = headingTextBox.Text.Trim();

        if (string.IsNullOrEmpty(input))
        {
            announcer.AnnounceImmediate("Please enter a heading value");
            headingTextBox.Focus();
            return;
        }

        if (!double.TryParse(input, out double value))
        {
            announcer.AnnounceImmediate("Invalid number format");
            headingTextBox.Focus();
            headingTextBox.SelectAll();
            return;
        }

        if (value < 0 || value > 360)
        {
            announcer.AnnounceImmediate("Heading must be between 0 and 360 degrees");
            headingTextBox.Focus();
            headingTextBox.SelectAll();
            return;
        }

        int targetHeading = (int)Math.Round(value);
        _ = aircraft.SetFCUHeading(targetHeading, simConnect, announcer);
        headingTextBox.SelectAll();
    }

    private void HandleButtonClick(string varKey)
    {
        // Call the aircraft's FCU variable setter with value 1 (button press)
        aircraft.SetFCUVariable(varKey, 1, simConnect, announcer);
    }
}
