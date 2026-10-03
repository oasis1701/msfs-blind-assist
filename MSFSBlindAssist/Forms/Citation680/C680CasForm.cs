using System.Runtime.InteropServices;
using MSFSBlindAssist.Aircraft.Citation680;

namespace MSFSBlindAssist.Forms.Citation680;

/// <summary>
/// The Alt+E window: the live CAS list (warnings first) then a blank line and the MFD's engine
/// strip, refreshed in place once a second so the caret stays put. F5 re-reads, Escape closes.
/// </summary>
public sealed class C680CasForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly C680CasMonitor _monitor;
    private readonly Func<Task<List<string>>> _engineStrip;
    private readonly DisplayListBox _box;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly IntPtr _previousWindow;
    private List<string> _eis = new() { "Engine strip: reading…" };
    private bool _refreshing;

    public C680CasForm(C680CasMonitor monitor, Func<Task<List<string>>> engineStrip)
    {
        _monitor = monitor; _engineStrip = engineStrip;
        _previousWindow = GetForegroundWindow();
        Text = "Citation Sovereign+ CAS and Engines";
        Size = new Size(560, 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true; ShowInTaskbar = true; KeyPreview = true;

        _box = new DisplayListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), AccessibleName = "CAS and engine indications", TabIndex = 0 };
        Controls.Add(_box);

        _timer.Tick += async (_, _) => await RefreshAsync();
        _monitor.Changed += Render;
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
            else if (e.KeyCode == Keys.F5) { e.Handled = true; await RefreshAsync(); }
        };
        Shown += async (_, _) => { _box.Focus(); await RefreshAsync(); _timer.Start(); };
        FormClosing += (_, _) =>
        {
            _timer.Stop(); _timer.Dispose();
            _monitor.Changed -= Render;
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var rows = await _engineStrip();
            if (rows.Count > 0) _eis = rows;
            else if (_eis.Count == 1 && _eis[0].StartsWith("Engine strip", StringComparison.Ordinal)) _eis = new List<string> { "Engine strip: the MFD is not answering (dark, or another window holds it)" };
        }
        catch { }
        finally { _refreshing = false; }
        Render();
    }

    private void Render()
    {
        if (IsDisposed) return;
        var lines = new List<string>(_monitor.Lines()) { "" };
        lines.AddRange(_eis);
        _box.SetLines(lines);
        if (_box.SelectedIndex < 0 && _box.Items.Count > 0) _box.SelectedIndex = 0;
    }
}
