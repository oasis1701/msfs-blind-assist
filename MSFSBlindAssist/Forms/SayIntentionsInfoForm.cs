using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Read-only view of the SayIntentions flight information, navigated line by line.
///
/// This replaces a single spoken run-on sentence. That was fine while the readout held
/// three facts; with the ATIS, the active runway configuration, the METAR and the TAF
/// in it, speaking the lot leaves a blind pilot no way to re-hear one part without
/// hearing all of it — and no way to stop it. A read-only multi-line text box is the
/// control screen readers navigate best: arrow keys move a line at a time, the reader
/// speaks each line as the caret lands, and Ctrl+Home / Ctrl+End jump the extremes.
///
/// WordWrap is deliberately OFF. Wrapped, a 400-character METAR becomes five visual
/// lines and Down-arrow walks the fragments, so the reader speaks a sliced-up METAR;
/// unwrapped, one logical line is one arrow press and one utterance. Long prose is
/// split into real lines by the report builder instead, which is a decision about
/// meaning rather than about pixel width.
/// </summary>
public class SayIntentionsInfoForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IntPtr _previousWindow;
    private TextBox _infoTextBox = null!;
    private Button _closeButton = null!;

    public SayIntentionsInfoForm(IReadOnlyList<string> lines)
    {
        _previousWindow = GetForegroundWindow();
        InitializeComponent(lines);
    }

    private void InitializeComponent(IReadOnlyList<string> lines)
    {
        Text = "SayIntentions Flight Information";
        Size = new Size(700, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = true;

        _infoTextBox = new TextBox
        {
            // The box is created with its content already in place so the screen
            // reader has something to speak the moment focus arrives.
            Text = string.Join(Environment.NewLine, lines),
            Location = new Point(12, 12),
            Size = new Size(660, 420),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BorderStyle = BorderStyle.Fixed3D,
            BackColor = SystemColors.Control,
            Font = new Font("Consolas", 10),
            AccessibleName = "SayIntentions flight information",
            AccessibleDescription =
                "Read-only. Use the arrow keys to read line by line, " +
                "Control Home and Control End for the start and end."
        };

        _closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(292, 442),
            Size = new Size(100, 32),
            Anchor = AnchorStyles.Bottom,
            AccessibleName = "Close",
            AccessibleDescription = "Close the SayIntentions flight information window"
        };
        _closeButton.Click += (_, _) => Close();

        _infoTextBox.TabIndex = 0;
        _closeButton.TabIndex = 1;

        Controls.Add(_infoTextBox);
        Controls.Add(_closeButton);

        CancelButton = _closeButton;

        Load += (_, _) =>
        {
            BringToFront();
            Activate();
            _infoTextBox.Focus();
            // Caret at the very start, with nothing selected. Without this the box
            // opens with its whole contents selected, and the reader announces the
            // entire report in one breath — the exact behaviour this window exists to
            // replace.
            _infoTextBox.SelectionStart = 0;
            _infoTextBox.SelectionLength = 0;
        };
    }

    /// <summary>Escape closes, like every other read-only window in the app.</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
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

        // Hand the foreground back to whatever had it — normally the simulator.
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }
}
