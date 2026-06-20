using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// On-demand EICAS crew-alert window for the HorizonSim 787 (Alt+E, output mode). Shows the active
/// warnings / cautions / advisories as a navigable read-only text box (arrow keys to read, Escape to
/// close) instead of a one-shot spoken read-back. A 1 s timer refreshes the text so newly-posted
/// alerts appear while the window is open; the caret position is preserved so a reader isn't yanked
/// to the top on refresh. The text comes from the always-on CoherentHS787CasClient.
/// </summary>
public class HS787EicasForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Func<string> _provider;
    private TextBox _box = null!;
    private System.Windows.Forms.Timer _timer = null!;
    private IntPtr _previousWindow;
    private string _lastText = "";

    public HS787EicasForm(Func<string> provider)
    {
        _provider = provider;
        Build();
    }

    private void Build()
    {
        Text = "787 EICAS Alerts";
        Size = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        _box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f),
            WordWrap = true,
            AccessibleName = "EICAS alerts",
            TabIndex = 0
        };
        Controls.Add(_box);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshText();

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
        FormClosing += (_, e) =>
        {
            _timer.Stop();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        RefreshText();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _box.SelectionStart = 0;
        _box.Focus();
        _timer.Start();
    }

    private void RefreshText()
    {
        string text;
        try { text = _provider() ?? ""; } catch { return; }
        if (text == _lastText) return;          // unchanged — don't touch the box (preserves caret/selection)
        int caret = _box.SelectionStart;
        _box.Text = text;
        _box.SelectionStart = Math.Min(caret, _box.TextLength);
        _box.SelectionLength = 0;
        _lastText = text;
    }
}
