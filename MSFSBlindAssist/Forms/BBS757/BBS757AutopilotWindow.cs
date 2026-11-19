using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>One button of the 757 autopilot window: its label, its live state (empty for none), what a press does and its Alt key.</summary>
public sealed record Bbs757AutopilotButton(string Label, Func<string> State, Action Press, char? Key = null);

/// <summary>
/// The "757 Autopilot" window (Ctrl+P in input mode), the iFly, HS787 and MD-11 windows' shape:
/// one button per MCP engage control (CMD L, C and R, A/T Arm, the two F/D switches, THR, SPD,
/// LNAV, VNAV, FLCH, HDG HOLD, ALT HOLD, V/S, LOC, APP, Speed Intervene, A/T Disconnect, and A/P
/// Disconnect, the captain's yoke button), each captioned with its live state ("CMD L: On"; a button with no state shows its
/// label alone), refreshed twice a second and rewritten only on change. Alt and a button's fixed
/// letter presses it: the window handles the keys itself rather than through an underlined caption
/// letter, since several labels hold no free letter of their own (ALT HOLD is Alt+E), and each
/// button names its key to the screen reader ("Alt+E"). A press goes through the
/// definition, which replays the cockpit's own click; it says nothing on success (CORE-7: the
/// caption's change is read by the screen reader), only "… unavailable" when nothing could be sent.
/// What each button shows and does is the definition's (BBS757Definition.Autopilot.cs); this
/// window only lays them out.
///
/// Hide on close (Escape and Close), reused for the life of the aircraft; the definition disposes
/// it on a profile switch.
/// </summary>
public sealed class BBS757AutopilotWindow : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int RefreshMs = 500;

    private readonly IReadOnlyList<Bbs757AutopilotButton> _items;
    private readonly Action? _requestStates;
    private readonly Func<IReadOnlyList<string>> _fmaRows;
    private DisplayListBox _fmaList = null!;
    private IReadOnlyList<string> _fmaShown = Array.Empty<string>();
    private readonly List<(Button Button, Bbs757AutopilotButton Item)> _buttons = new();
    private readonly System.Windows.Forms.Timer _refresh;
    private IntPtr _previousWindow;
    private int _tick;

    /// <param name="items">The buttons, in order.</param>
    /// <param name="requestStates">Asks the sim for the states the cache does not stream; called about once a second while shown.</param>
    /// <param name="fmaRows">The flight mode annunciator's rows (the reader's four, or one status row), asked on every refresh.</param>
    public BBS757AutopilotWindow(IReadOnlyList<Bbs757AutopilotButton> items, Action? requestStates, Func<IReadOnlyList<string>> fmaRows)
    {
        _items = items;
        _requestStates = requestStates;
        _fmaRows = fmaRows;
        BuildForm();
        _refresh = new System.Windows.Forms.Timer { Interval = RefreshMs };
        _refresh.Tick += (_, _) => Tick();
    }

    private void BuildForm()
    {
        Text = "757 Autopilot";
        AccessibleName = "757 Autopilot";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        const int left = 15, col2 = 215, width = 190, height = 34, row = 42;
        // The flight mode annunciator first (S6): a list the reader's cursor can walk, before the buttons.
        int tab = 0;
        _fmaList = new DisplayListBox
        {
            Location = new Point(left, 15),
            Size = new Size(col2 + width - left, 90),
            AccessibleName = "Flight mode annunciator",
            Name = "Flight mode annunciator",
            TabIndex = tab++,
        };
        Controls.Add(_fmaList);
        int y = 15 + 90 + 12;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var button = new KeyedButton(item.Key)
            {
                Location = new Point(i % 2 == 0 ? left : col2, y),
                Size = new Size(width, height),
                TabIndex = tab++,
                UseMnemonic = false,   // the window's own keys, never a letter in a label or state
            };
            button.Click += (_, _) =>
            {
                item.Press();
                RefreshCaptions(force: false);
            };
            _buttons.Add((button, item));
            Controls.Add(button);
            if (i % 2 == 1 || i == _items.Count - 1) y += row;
        }

        var close = new Button
        {
            Text = "Close",
            Location = new Point(left, y),
            Size = new Size(col2 + width - left, height),
            TabIndex = tab,
        };
        close.Click += (_, _) => HideWindow();
        Controls.Add(close);
        CancelButton = close;   // Escape
        ClientSize = new Size(col2 + width + left, y + height + 15);

        // Real shutdown goes through; any other close hides, so the window keeps its place.
        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
                return;
            e.Cancel = true;
            HideWindow();
        };
        RefreshCaptions(force: true);
        RefreshFma();
    }

    /// <summary>The list's next rows from the monitor, set only when they differ (a stable row is never re-read); a failure keeps the rows shown.</summary>
    internal void RefreshFma()
    {
        IReadOnlyList<string> rows;
        try { rows = _fmaRows(); }
        catch (Exception) { return; }
        if (_fmaShown.SequenceEqual(rows)) return;
        _fmaShown = rows.ToArray();
        _fmaList.SetLines(_fmaShown);
    }

    /// <summary>Alt and a button's letter presses it, as a click would.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.Modifiers) == Keys.Alt)
        {
            Keys key = keyData & Keys.KeyCode;
            foreach (var (button, item) in _buttons)
            {
                if (item.Key is char k && key == (Keys)char.ToUpperInvariant(k))
                {
                    button.Focus();
                    button.PerformClick();
                    return true;
                }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        _requestStates?.Invoke();
        RefreshCaptions(force: true);
        RefreshFma();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _fmaList.Focus();
        _refresh.Start();
    }

    private void HideWindow()
    {
        _refresh.Stop();
        Hide();
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    private void Tick()
    {
        if (IsDisposed || !Visible) return;
        if (++_tick % 2 == 0) _requestStates?.Invoke();
        RefreshCaptions(force: false);
        RefreshFma();
    }

    /// <summary>
    /// Every caption from its state, "Label: State", rewritten only when it changed so a screen reader
    /// is never re-read a stable caption.
    /// </summary>
    private void RefreshCaptions(bool force)
    {
        foreach (var (button, item) in _buttons)
        {
            string text;
            try { text = item.State() is { Length: > 0 } state ? $"{item.Label}: {state}" : item.Label; }
            catch (Exception) { text = $"{item.Label}: unavailable"; }
            if (!force && button.Text == text) continue;
            button.Text = text;
            button.AccessibleName = text;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _refresh?.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        // Hide-on-close: Close() is canceled, so the timer is torn down here.
        if (disposing)
        {
            _refresh?.Stop();
            _refresh?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>A button that names its window-handled Alt key to the screen reader (its accessible keyboard shortcut).</summary>
internal sealed class KeyedButton : Button
{
    private readonly char? _key;

    public KeyedButton(char? key) => _key = key;

    protected override AccessibleObject CreateAccessibilityInstance() => new KeyedAccessibleObject(this);

    private sealed class KeyedAccessibleObject : ButtonBaseAccessibleObject
    {
        private readonly KeyedButton _owner;

        public KeyedAccessibleObject(KeyedButton owner) : base(owner) => _owner = owner;

        public override string? KeyboardShortcut =>
            _owner._key is char k ? $"Alt+{char.ToUpperInvariant(k)}" : base.KeyboardShortcut;
    }
}
