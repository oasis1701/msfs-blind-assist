using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// On-demand EICAS crew-alert window for the HorizonSim 787 (Alt+E, output mode). Shows the active
/// warnings / cautions / advisories as a navigable read-only list (arrow keys to read, Escape to
/// close) instead of a one-shot spoken read-back. A 1 s timer refreshes the text so newly-posted
/// alerts appear while the window is open; the refresh reconciles the list in place (via
/// DisplayListBox, which wraps DisplayList.UpdateInPlace) so the reader's selected row is preserved
/// instead of being reset to the top. The text comes from the always-on CoherentHS787CasClient.
/// </summary>
public class HS787EicasForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Func<string> _provider;
    private DisplayListBox _box = null!;
    private System.Windows.Forms.Timer _timer = null!;
    private IntPtr _previousWindow;

    public HS787EicasForm(Func<string> provider)
    {
        _provider = provider;
        Build();
    }

    private void Build()
    {
        Text = "787 EICAS";
        Size = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        _box = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f),
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
        _box.Focus();
        _timer.Start();
    }

    private void RefreshText()
    {
        string text;
        try { text = _provider() ?? ""; } catch { return; }   // keep the existing provider call + guard style
        _box.SetText(text);
    }
}
