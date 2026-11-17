using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// The one window the 757's display readers open: a named live list of the reader's rows, then an optional
/// controls area below it in the tab order. A 1 s timer asks for the rows and rewrites the list only when they
/// differ, so the screen reader's cursor never jumps and only a changed row is re-read. Escape hides the window
/// and returns to the previous one (the PMDG737EngineDisplaysForm pattern); hiding keeps the window for reuse,
/// and only a real shutdown, <see cref="CloseForGood"/> (the 757's switch away, for a window the readers' registry
/// owns) or its owner's Dispose ends it. A rows provider that throws keeps the rows shown.
/// What the rows say is the reader's; this window only lays them out.
/// </summary>
internal sealed class BBS757DisplayForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Func<IReadOnlyList<string>> _rows;
    private readonly DisplayListBox _list;
    private readonly System.Windows.Forms.Timer _timer;
    private IReadOnlyList<string> _shown = Array.Empty<string>();
    private IntPtr _previousWindow;
    private bool _closingForGood;

    public BBS757DisplayForm(string title, string listName, Func<IReadOnlyList<string>> rows, Control? controls = null)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        Text = title;
        AccessibleName = title;
        Size = new Size(560, controls == null ? 360 : 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = true;
        KeyPreview = true;

        _list = new DisplayListBox
        {
            Location = new Point(12, 12),
            Size = new Size(520, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | (controls == null ? AnchorStyles.Bottom : 0),
            AccessibleName = listName,
            Name = listName,
            TabIndex = 0,
        };
        Controls.Add(_list);
        if (controls != null)
        {
            controls.Location = new Point(12, _list.Bottom + 8);
            controls.TabIndex = 1;
            Controls.Add(controls);
            // A controls area that wraps onto more rows than the room below the list holds (the EHSI's ten EFIS
            // controls) makes the window taller; a smaller one (the upper EICAS's three buttons) leaves it as it was.
            int needed = controls.Top + controls.GetPreferredSize(new Size(_list.Width, 0)).Height + 12;
            if (ClientSize.Height < needed) ClientSize = new Size(ClientSize.Width, needed);
        }

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => { if (!IsDisposed && Visible) RefreshRows(); };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            HideWindow();
        };
        // Any close other than the real shutdown or CloseForGood hides, so the window keeps its place.
        FormClosing += (_, e) =>
        {
            if (_closingForGood
                || e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
                return;
            e.Cancel = true;
            HideWindow();
        };
        RefreshRows();
    }

    /// <summary>Shows the window, refreshes at once and puts the cursor in the list.</summary>
    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        RefreshRows();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _list.Focus();
        _timer.Start();
    }

    /// <summary>The list's next rows, set only when they differ; a failing provider keeps the rows shown.</summary>
    internal void RefreshRows()
    {
        IReadOnlyList<string> rows;
        try { rows = _rows(); }
        catch (Exception) { return; }
        if (_shown.SequenceEqual(rows)) return;
        _shown = rows.ToArray();
        _list.SetLines(_shown);
    }

    /// <summary>Ends the window: its timer stops, the close is not cancelled, and it is disposed. The 757 definition's
    /// switch away does this to every window its readers own (<c>Bbs757DisplayRegistry.OwnWindow</c>), so none stays up
    /// showing a stopped monitor. On the UI thread; again, nothing.</summary>
    public void CloseForGood()
    {
        if (IsDisposed) return;
        _closingForGood = true;
        _timer.Stop();
        Close();
        if (!IsDisposed) Dispose();
    }

    private void HideWindow()
    {
        _timer.Stop();
        Hide();
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _timer?.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer?.Dispose();
        base.Dispose(disposing);
    }
}
