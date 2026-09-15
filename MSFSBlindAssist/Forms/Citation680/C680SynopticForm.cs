using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.Citation680;

/// <summary>
/// The Alt+S window: one MFD half (the crew seat's) as a list — the pane's title then its
/// content in reading order — refreshed in place every 1.5 s. The Page combo selects a
/// synoptic (or the checklist) by pressing the MFD touchscreen's own buttons: Home,
/// Aircraft Systems, then the page. F5 re-reads, Escape closes.
/// </summary>
public sealed class C680SynopticForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// The MFD touchscreen's Aircraft Systems SYNOPTICS plus the Home page's Checklist. The same page's
    /// "Controls" buttons (Systems Test, Cabin Management, Exterior Lights, Temp, Propulsion, Cabin
    /// Pressure) open TOUCHSCREEN pages and leave the MFD pane on the previous synoptic (measured
    /// 2026-09-15), so they are not listed here: they are read in the touchscreen window.
    /// </summary>
    public static readonly string[] Pages = { "Summary", "Hydraulics", "Fuel", "Electrical", "Checklist" };

    private readonly Func<Task<List<string>>> _paneRows;
    private readonly Func<string, Task<string>> _selectPage;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly DisplayListBox _box;
    private readonly ComboBox _page;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly IntPtr _previousWindow;
    private bool _busy;

    public C680SynopticForm(string sideName, Func<Task<List<string>>> paneRows, Func<string, Task<string>> selectPage, ScreenReaderAnnouncer announcer)
    {
        _paneRows = paneRows; _selectPage = selectPage; _announcer = announcer;
        _previousWindow = GetForegroundWindow();
        Text = $"Citation Sovereign+ MFD, {sideName} pane";
        Size = new Size(600, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true; ShowInTaskbar = true; KeyPreview = true;

        var label = new Label { Text = "Page", AutoSize = true, Location = new Point(12, 14) };
        _page = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(60, 10), Width = 200, AccessibleName = "Page", TabIndex = 1 };
        _page.Items.Add("(as shown)");
        _page.Items.AddRange(Pages);
        _page.SelectedIndex = 0;
        _page.SelectedIndexChanged += async (_, _) => { if (_page.SelectedIndex > 0) await SelectAsync((string)_page.SelectedItem!); };

        _box = new DisplayListBox
        {
            Location = new Point(12, 44), Size = new Size(560, 420), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 10f), AccessibleName = "MFD pane", TabIndex = 0
        };
        _box.SetText("Reading the MFD…");
        Controls.Add(label); Controls.Add(_page); Controls.Add(_box);

        _timer.Tick += async (_, _) => await RefreshAsync();
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
            else if (e.KeyCode == Keys.F5) { e.Handled = true; await RefreshAsync(); }
        };
        Shown += async (_, _) => { _box.Focus(); await RefreshAsync(); _timer.Start(); };
        FormClosing += (_, _) => { _timer.Stop(); _timer.Dispose(); if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow); };
    }

    private async Task SelectAsync(string page)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            string result = await _selectPage(page);
            if (result.Length > 0) _announcer.AnnounceImmediate(result);
            await Task.Delay(600);
        }
        finally { _busy = false; }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_busy || IsDisposed) return;
        List<string> rows;
        try { rows = await _paneRows(); } catch { rows = new List<string>(); }
        if (IsDisposed) return;
        if (rows.Count == 0) { if (_box.Items.Count <= 1) _box.SetText("The MFD is not answering (dark, or another window holds it)."); return; }
        int keep = _box.SelectedIndex;
        _box.SetLines(rows);
        if (keep >= 0 && keep < _box.Items.Count && _box.SelectedIndex < 0) _box.SelectedIndex = keep;
        if (_box.SelectedIndex < 0 && _box.Items.Count > 0) _box.SelectedIndex = 0;
    }
}
