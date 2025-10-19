
namespace FBWBA.Forms;
/// <summary>
/// Custom dialog for database/simulator mismatch warnings.
/// Uses Windows API to force focus even when simulator is in foreground.
/// </summary>
public class DatabaseMismatchDialog : Form
{
    // Windows API for forcing window to foreground
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private Label messageLabel = null!;
    private Button yesButton = null!;
    private Button noButton = null!;
    private Button cancelButton = null!;
    private PictureBox iconBox = null!;

    public DatabaseMismatchDialog(string message, string detectedSim, string configuredDb)
    {
        InitializeComponent();
        messageLabel.Text = message;
        Text = "Database Mismatch Warning";

        // Force focus when shown
        Load += DatabaseMismatchDialog_Load;
    }

    private void InitializeComponent()
    {
        Size = new Size(500, 250);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true; // Show in taskbar for easier access
        TopMost = true; // Start as topmost

        // Warning icon
        iconBox = new PictureBox
        {
            Location = new Point(20, 20),
            Size = new Size(48, 48),
            Image = SystemIcons.Warning.ToBitmap()
        };

        // Message label
        messageLabel = new Label
        {
            Location = new Point(80, 20),
            Size = new Size(400, 120),
            AutoSize = false,
            Font = new Font(Font.FontFamily, 10),
            AccessibleName = "Warning message"
        };

        // Yes button
        yesButton = new Button
        {
            Text = "&Yes - Open Settings",
            Location = new Point(80, 160),
            Size = new Size(130, 35),
            DialogResult = DialogResult.Yes,
            AccessibleName = "Yes, open database settings",
            AccessibleDescription = "Open database settings to fix the mismatch"
        };

        // No button
        noButton = new Button
        {
            Text = "&No - Continue Anyway",
            Location = new Point(220, 160),
            Size = new Size(130, 35),
            DialogResult = DialogResult.No,
            AccessibleName = "No, continue anyway",
            AccessibleDescription = "Continue with current database despite mismatch"
        };

        // Cancel button
        cancelButton = new Button
        {
            Text = "&Cancel",
            Location = new Point(360, 160),
            Size = new Size(100, 35),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel the teleport operation"
        };

        // Add controls
        Controls.AddRange(new Control[]
        {
            iconBox, messageLabel, yesButton, noButton, cancelButton
        });

        AcceptButton = yesButton;
        CancelButton = cancelButton;

        // Tab order
        yesButton.TabIndex = 0;
        noButton.TabIndex = 1;
        cancelButton.TabIndex = 2;
    }

    private void DatabaseMismatchDialog_Load(object? sender, EventArgs e)
    {
        // Force window to foreground using multiple techniques
        BringToFront();
        Focus();

        IntPtr handle = Handle;

        // Make it topmost temporarily
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        // Show and restore
        ShowWindow(handle, SW_RESTORE);
        ShowWindow(handle, SW_SHOW);

        // Bring to top
        BringWindowToTop(handle);

        // Set foreground
        SetForegroundWindow(handle);

        // Remove topmost after a short delay so it doesn't stay on top
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (s, ev) =>
        {
            SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            TopMost = false;
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();

        // Focus the first button
        yesButton.Focus();
    }

    /// <summary>
    /// Shows the database mismatch warning dialog
    /// </summary>
    public static DialogResult ShowMismatchWarning(string detectedSim, string configuredDb)
    {
        string message = detectedSim == "FS2024"
            ? "Warning: You are running Flight Simulator 2024 but have\n" +
              "FS2020 database selected.\n\n" +
              "This may provide incorrect runway coordinates and airport data.\n\n" +
              "Would you like to switch to FS2024 database now?"
            : "Warning: You are running Flight Simulator 2020 but have\n" +
              "FS2024 database selected.\n\n" +
              "This may provide incorrect airport data.\n\n" +
              "Would you like to switch to FS2020 database now?";

        using (var dialog = new DatabaseMismatchDialog(message, detectedSim, configuredDb))
        {
            return dialog.ShowDialog();
        }
    }
}
