using System.Runtime.InteropServices;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Form to display Gemini AI analysis results of cockpit displays.
/// </summary>
public partial class DisplayReadingResultForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private RichTextBox resultTextBox = null!;
    private Button closeButton = null!;
    private Label titleLabel = null!;

    private readonly IntPtr previousWindow;

    public DisplayReadingResultForm(string displayName, string analysisResult)
        : this(displayName, analysisResult, "Analysis")
    {
    }

    public DisplayReadingResultForm(string displayName, string analysisResult, string analysisType)
    {
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();

        InitializeComponent(displayName, analysisResult, analysisType);
        SetupAccessibility();
    }

    private void InitializeComponent(string displayName, string analysisResult, string analysisType)
    {
        Text = $"{displayName} {analysisType} - Gemini AI";
        Size = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;  // Allow resizing for long text
        MinimumSize = new Size(600, 400);
        ShowInTaskbar = false;

        // Title Label
        titleLabel = new Label
        {
            Text = $"{displayName} - AI {analysisType}",
            Location = new Point(20, 20),
            Size = new Size(740, 25),
            Font = new Font("Microsoft Sans Serif", 12, FontStyle.Bold),
            AccessibleName = $"{displayName} {analysisType} Title"
        };

        // Result RichTextBox (read-only, better accessibility for screen readers)
        resultTextBox = new RichTextBox
        {
            Location = new Point(20, 55),
            Size = new Size(740, 450),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            AccessibleName = $"{displayName} {analysisType} Result",
            AccessibleDescription = $"Gemini AI {analysisType.ToLower()} of {displayName}",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            Text = analysisResult,
            WordWrap = true
        };

        // Close Button
        closeButton = new Button
        {
            Text = "&Close (ESC)",
            Location = new Point(685, 520),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close analysis window"
        };
        closeButton.Click += CloseButton_Click;

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            titleLabel, resultTextBox, closeButton
        });

        CancelButton = closeButton;
        KeyPreview = true;

        // Handle form resize to adjust controls
        Resize += (sender, e) =>
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;

            titleLabel.Width = width - 40;
            resultTextBox.Width = width - 40;
            resultTextBox.Height = height - 125;
            closeButton.Location = new Point(width - 95, height - 50);
        };
    }

    private void SetupAccessibility()
    {
        // Set tab order for logical navigation
        resultTextBox.TabIndex = 0;
        closeButton.TabIndex = 1;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            resultTextBox.Focus();
            resultTextBox.SelectionStart = 0;  // Start at beginning of text
        };
    }

    private void CloseButton_Click(object? sender, EventArgs e)
    {
        Close();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        // Restore focus to the previous window (likely the simulator)
        if (previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(previousWindow);
        }
    }
}
