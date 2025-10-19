using FBWBA.Accessibility;
using FBWBA.SimConnect;

namespace FBWBA.Forms;
public partial class StatusDisplayForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TextBox statusTextBox = null!;
    private Button refreshButton = null!;
    private Button closeButton = null!;
    private Label titleLabel = null!;

    private readonly ScreenReaderAnnouncer _announcer = null!;
    private readonly SimConnectManager _simConnectManager = null!;

    // Store STATUS page line values (36 total: 18 LEFT + 18 RIGHT)
    private readonly Dictionary<string, long> _statusLines = new Dictionary<string, long>();

    private readonly IntPtr previousWindow;

    public StatusDisplayForm(ScreenReaderAnnouncer announcer, SimConnectManager simConnectManager)
    {
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();

        _announcer = announcer;
        _simConnectManager = simConnectManager;
        InitializeComponent();
        SetupAccessibility();

        // Subscribe to SimVar updates
        if (_simConnectManager != null)
        {
            _simConnectManager.SimVarUpdated += OnSimVarUpdated;
        }

        RefreshStatusData(); // Load initial data
    }

    private void InitializeComponent()
    {
        Text = "ECAM STATUS - FlyByWire A32NX";
        Size = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Title Label
        titleLabel = new Label
        {
            Text = "ECAM STATUS - FlyByWire A32NX",
            Location = new Point(20, 20),
            Size = new Size(400, 20),
            Font = new Font("Microsoft Sans Serif", 10, FontStyle.Bold),
            AccessibleName = "ECAM Status Display Title"
        };

        // Status TextBox (read-only, multiline)
        statusTextBox = new TextBox
        {
            Location = new Point(20, 50),
            Size = new Size(740, 450),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "ECAM STATUS Messages",
            AccessibleDescription = "ECAM STATUS page showing system information and inoperative systems",
            Font = new Font("Consolas", 10, FontStyle.Regular),
            Text = "Loading STATUS data..."
        };

        // Refresh Button
        refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(600, 520),
            Size = new Size(75, 30),
            AccessibleName = "Refresh",
            AccessibleDescription = "Refresh STATUS data from simulator"
        };
        refreshButton.Click += RefreshButton_Click;

        // Close Button
        closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(685, 520),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close STATUS window"
        };
        closeButton.Click += CloseButton_Click;

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            titleLabel, statusTextBox, refreshButton, closeButton
        });

        CancelButton = closeButton;
        KeyPreview = true;
    }

    private void SetupAccessibility()
    {
        // Set tab order for logical navigation
        statusTextBox.TabIndex = 0;
        refreshButton.TabIndex = 1;
        closeButton.TabIndex = 2;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            statusTextBox.Focus();
        };
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        RefreshStatusData();
        _announcer?.Announce("STATUS data refreshed");
    }

    private void CloseButton_Click(object? sender, EventArgs e)
    {
        Close();
    }

    private void RefreshStatusData()
    {
        try
        {
            // Show loading message
            statusTextBox.Text = "Loading STATUS data...";

            // Clear existing data
            _statusLines.Clear();

            // Request all 36 STATUS variables (18 LEFT + 18 RIGHT)
            _simConnectManager?.RequestStatusMessages();

            System.Diagnostics.Debug.WriteLine("[StatusDisplayForm] STATUS data requested, waiting for response");
        }
        catch (Exception ex)
        {
            statusTextBox.Text = $"Error loading STATUS data: {ex.Message}";
        }
    }

    private string FormatStatusData()
    {
        var data = new System.Text.StringBuilder();

        // Process LEFT side (lines 1-18)
        bool hasLeftMessages = false;
        var leftMessages = new List<string>();

        for (int i = 1; i <= 18; i++)
        {
            string key = $"A32NX_STATUS_LEFT_LINE_{i}";
            if (_statusLines.ContainsKey(key) && _statusLines[key] != 0)
            {
                long code = _statusLines[key];
                string message = EWDMessageLookup.GetMessage(code);
                string rawMessage = EWDMessageLookup.GetRawMessage(code);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    string color = EWDMessageLookup.GetMessagePriority(rawMessage);
                    if (!string.IsNullOrEmpty(color))
                    {
                        leftMessages.Add($"{message} ({color})");
                    }
                    else
                    {
                        leftMessages.Add(message);
                    }
                    hasLeftMessages = true;
                }
            }
        }

        // Process RIGHT side (lines 1-18)
        bool hasRightMessages = false;
        var rightMessages = new List<string>();

        for (int i = 1; i <= 18; i++)
        {
            string key = $"A32NX_STATUS_RIGHT_LINE_{i}";
            if (_statusLines.ContainsKey(key) && _statusLines[key] != 0)
            {
                long code = _statusLines[key];
                string message = EWDMessageLookup.GetMessage(code);
                string rawMessage = EWDMessageLookup.GetRawMessage(code);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    string color = EWDMessageLookup.GetMessagePriority(rawMessage);
                    if (!string.IsNullOrEmpty(color))
                    {
                        rightMessages.Add($"{message} ({color})");
                    }
                    else
                    {
                        rightMessages.Add(message);
                    }
                    hasRightMessages = true;
                }
            }
        }

        // Build output - two column format
        if (hasLeftMessages)
        {
            data.AppendLine("LEFT SIDE");
            foreach (var msg in leftMessages)
            {
                data.AppendLine(msg);
            }
            data.AppendLine(); // Single blank line separator
        }

        if (hasRightMessages)
        {
            data.AppendLine("RIGHT SIDE");
            foreach (var msg in rightMessages)
            {
                data.AppendLine(msg);
            }
            data.AppendLine(); // Single blank line separator
        }

        if (!hasLeftMessages && !hasRightMessages)
        {
            data.AppendLine("No messages");
            data.AppendLine();
        }

        data.AppendLine("Press F5 to refresh, Press ESC to close");

        return data.ToString();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Handle F5 key for refresh
        if (keyData == Keys.F5)
        {
            RefreshStatusData();
            _announcer?.Announce("STATUS data refreshed");
            return true;
        }

        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        // Check if this is a STATUS line variable
        if (e.VarName.StartsWith("A32NX_STATUS_LEFT_LINE_") ||
            e.VarName.StartsWith("A32NX_STATUS_RIGHT_LINE_"))
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                return;
            }

            // Store the numeric code
            _statusLines[e.VarName] = (long)e.Value;

            // Update display when we have all variables
            // (simple approach: update on every variable received)
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        // Update the display with formatted data
        statusTextBox.Text = FormatStatusData();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Unsubscribe from events
        if (_simConnectManager != null)
        {
            _simConnectManager.SimVarUpdated -= OnSimVarUpdated;
        }
        base.OnFormClosed(e);

        // Restore focus to the previous window (likely the simulator)
        if (previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(previousWindow);
        }
    }
}
