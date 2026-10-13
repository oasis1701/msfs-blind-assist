using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.A300;

/// <summary>
/// The iniBuilds A300's two MCDUs as text (Shift+M). The screens come from the aircraft's own MCDU
/// export (<see cref="SimConnect.A300.A300McduDataManager"/>), the keys go out as the cockpit's own
/// key L:vars (<see cref="IniA300Definition.PressMcduKeys"/>).
///
/// Built on the MD-11 MCDU window's rules: the list is the title, each label above its numbered
/// line and the scratchpad, and the cursor follows the ROW it was on, never its index; it polls only
/// while visible, and a re-show catches up silently; a page change is spoken by its title and puts
/// the cursor on line 1; the scratchpad is read back once a burst of keys has settled; the window's
/// keys never take a chord the focused unit selector needs (<see cref="A300McduKeyRouting"/>).
/// </summary>
public sealed class A300McduForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IniA300Definition _definition;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr _previousWindow = IntPtr.Zero;

    private ComboBox _unitSelector = null!;
    private TextBox _statusBox = null!;
    private DisplayListBox _display = null!;
    private TextBox _input = null!;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 250 };

    private A300McduUnit _unit = A300McduUnit.Captain;
    private A300McduScreen? _rendered;
    private IReadOnlyList<A300McduRow>? _rows;
    private readonly Dictionary<A300McduUnit, string> _lastTitle = new();
    private readonly CduScratchpadAnnouncer _scratchpad = new("Scratchpad cleared", stablePolls: 2);
    private readonly A300McduBlankClocks _blank = new();
    private bool _clearing;

    /// <summary>Beyond the last queued key: the aircraft applying it, the client-data delivery and the
    /// next poll. A margin, not a measurement — too short reads an entry half-typed.</summary>
    private const int SettleMarginMs = 400;

    /// <summary>The clear-all's backstop against an unreadable feed.</summary>
    private const int MaxClearPresses = A300McduText.Cols + 4;

    public A300McduForm(IniA300Definition definition, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _definition = definition;
        _sim = sim;
        _announcer = announcer;
        BuildLayout();
        MonitorManagerShared.HideOnClose(this, () =>
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        });
        _poll.Tick += (_, _) => Poll();
    }

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void BuildLayout()
    {
        SuspendLayout();
        Text = "A300 MCDU";
        AccessibleName = "A300 MCDU";
        ClientSize = new Size(620, 760);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;
        _unitSelector = new ComboBox
        {
            Location = new Point(10, y),
            Size = new Size(220, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "MCDU unit",
        };
        _unitSelector.Items.AddRange(new object[] { "Captain", "First Officer" });
        _unitSelector.SelectedIndex = 0;

        _statusBox = new TextBox
        {
            Text = "Waiting for data",
            Location = new Point(240, y),
            Size = new Size(370, 25),
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "MCDU status",
        };
        y += 32;

        _display = new DisplayListBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 290),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU Display",
            AccessibleDescription = "Current MCDU screen. Use arrow keys to read lines.",
            IntegralHeight = false,
        };
        y += 300;

        _input = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 25),
            AccessibleName = "MCDU Input",
            AccessibleDescription = "Type text and press Enter to send it to the MCDU scratchpad.",
        };
        y += 34;

        var buttons = new List<Button>();
        int width = 116, height = 30, gap = 5, perRow = 5;
        for (int i = 0; i < A300McduKeys.PageButtons.Length; i++)
        {
            var (label, key) = A300McduKeys.PageButtons[i];
            var button = new Button
            {
                Text = label,
                Location = new Point(10 + (i % perRow) * (width + gap), y + (i / perRow) * (height + gap)),
                Size = new Size(width, height),
            };
            button.Click += (_, _) => Press(key);
            buttons.Add(button);
        }

        Controls.Add(_unitSelector);
        Controls.Add(_statusBox);
        Controls.Add(_display);
        Controls.Add(_input);
        foreach (var b in buttons) Controls.Add(b);

        int tab = 0;
        _display.TabIndex = tab++;
        _input.TabIndex = tab++;
        _statusBox.TabIndex = tab++;
        _unitSelector.TabIndex = tab++;
        foreach (var b in buttons) b.TabIndex = tab++;

        _unitSelector.SelectedIndexChanged += (_, _) =>
        {
            _unit = _unitSelector.SelectedIndex == 1 ? A300McduUnit.FirstOfficer : A300McduUnit.Captain;
            Resync();   // silent: the screen reader already read the selector's new value
        };
        _display.KeyDown += Display_KeyDown;
        _input.KeyDown += Input_KeyDown;
        KeyDown += Form_KeyDown;
        ResumeLayout(false);
    }

    // ---------------------------------------------------------------------------------
    // Keys
    // ---------------------------------------------------------------------------------

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        var combo = ActiveControl as ComboBox;
        var focus = combo == null ? A300McduKeyRouting.ComboFocus.None
            : combo.DroppedDown ? A300McduKeyRouting.ComboFocus.Dropped
            : A300McduKeyRouting.ComboFocus.Closed;
        var action = A300McduKeyRouting.Resolve(e.KeyCode, e.Alt, e.Control, e.Shift, focus,
            MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys);
        if (action.Kind == A300McduKeyRouting.ActionKind.Pass)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        switch (action.Kind)
        {
            case A300McduKeyRouting.ActionKind.Press:
                Press(action.Key);
                break;
            case A300McduKeyRouting.ActionKind.Close:
                Close();
                break;
            case A300McduKeyRouting.ActionKind.SelectUnit:
                _unitSelector.SelectedIndex = action.Unit == A300McduUnit.FirstOfficer ? 1 : 0;
                if (!_unitSelector.Focused)
                    _announcer.Announce(_unitSelector.SelectedItem?.ToString() ?? "");
                break;
            case A300McduKeyRouting.ActionKind.FocusInput:
                _input.Focus();
                break;
            case A300McduKeyRouting.ActionKind.FocusDisplay:
                _display.Focus();
                break;
        }
    }

    /// <summary>Backspace (one CLR), Delete (clear the scratchpad) and Right (next page), on the
    /// display only: the input box needs those keys to edit its own text.</summary>
    private void Display_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Alt || e.Control || e.Shift)
            return;
        switch (e.KeyCode)
        {
            case Keys.Back:
                Press("CLR");
                break;
            case Keys.Delete:
                _ = ClearScratchpadAsync();
                break;
            case Keys.Right:
                Press("NEXT");
                break;
            default:
                return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Return || e.Alt || e.Control)
            return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        string text = _input.Text.ToUpperInvariant();
        if (text.Length == 0)
            return;
        // A refused entry stays in the box, to be edited and sent again.
        if (A300McduKeys.RefusalFor(text) is { } refusal)
        {
            _announcer.Announce(refusal);
            return;
        }
        var keys = text.Select(c => A300McduKeys.ForChar(c)!).ToList();
        if (!Send(keys))
            return;
        _input.Clear();
    }

    private bool Press(string key) => Send(new[] { key });

    /// <summary>Queues keys on the selected MCDU and holds the scratchpad read-back until they have
    /// landed, so an entry is read once, settled. Says why when nothing could be sent.</summary>
    private bool Send(IReadOnlyList<string> keys)
    {
        if (!_definition.PressMcduKeys(_unit, keys))
        {
            _announcer.Announce("MCDU unavailable");
            return false;
        }
        int pending = _definition.McduKeysPending;
        var until = DateTime.UtcNow.AddMilliseconds(pending * (A300McduKeys.HoldMs + A300McduKeys.GapMs) + SettleMarginMs);
        if (until > _scratchpad.SuppressUntil)
            _scratchpad.SuppressUntil = until;
        return true;
    }

    /// <summary>
    /// Clears the whole scratchpad: one CLR at a time, re-reading the screen after each has landed,
    /// and stopping the moment it reads empty. Never a CLR on an empty scratchpad — the A300 then
    /// puts "CLR" into it. Silent when it works (the read-back says "Scratchpad cleared").
    /// </summary>
    private async Task ClearScratchpadAsync()
    {
        if (_clearing)
            return;
        _clearing = true;
        try
        {
            var unit = _unit;
            for (int presses = 0; ; presses++)
            {
                var screen = _sim.A300McduDataManager?.GetScreen(unit);
                if (screen == null || screen.IsBlank)
                {
                    _announcer.Announce("Could not clear the scratchpad");
                    return;
                }
                if (string.IsNullOrWhiteSpace(screen.Scratchpad))
                {
                    if (presses == 0)
                        _announcer.Announce("Scratchpad already empty");
                    return;
                }
                if (presses >= MaxClearPresses || unit != _unit)
                {
                    _announcer.Announce("Could not clear the scratchpad");
                    return;
                }
                if (!Press("CLR"))
                    return;
                await Task.Delay(A300McduKeys.HoldMs + A300McduKeys.GapMs + 150);
                while (_definition.McduKeysPending > 0 && !IsDisposed)
                    await Task.Delay(50);
                if (IsDisposed)
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"MCDU scratchpad clear failed: {ex.Message}");
        }
        finally
        {
            _clearing = false;
        }
    }

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    private void Poll()
    {
        var manager = _sim.A300McduDataManager;
        if (manager == null)
        {
            _statusBox.Text = $"{UnitName}: not connected";
            return;
        }
        var screen = manager.GetScreen(_unit);
        if (screen == null)
        {
            _statusBox.Text = $"{UnitName}: waiting for data";
            return;
        }
        // The FMS's blank frame between pages: draw nothing, say nothing, leave the page and the
        // cursor where they are. Judged before the reference check: a blank that stays is delivered
        // once, so only a poll can tell that it has lasted.
        if (_blank.Judge(_unit, screen, DateTime.UtcNow) == A300McduDisplayAction.HoldLastPage)
            return;
        if (!ReferenceEquals(screen, _rendered))
            Render(screen, silent: false);
        if (!screen.IsBlank && _scratchpad.OnPoll(screen.Scratchpad.Trim(), DateTime.UtcNow) is { } say)
            _announcer.Announce(say);
        UpdateAnnunciators(silent: false);
    }

    /// <summary>Each unit's lit annunciators when last shown.</summary>
    private readonly Dictionary<A300McduUnit, IReadOnlyList<string>> _lit = new();

    /// <summary>
    /// The unit's lit annunciators in the status box ("Captain MCDU: MSG"), and one coming on spoken, as the
    /// MD-11's MCDU window does: MSG lighting is how the FMS says "read the scratchpad", with no text change
    /// behind it. The captain's MSG is left to its fault light, "MCDU message light", which speaks anywhere
    /// (<see cref="A300McduLights.ComingOn"/>), so it is not said twice. A unit switch or a re-show takes the
    /// lights as they are, silently.
    /// </summary>
    private void UpdateAnnunciators(bool silent)
    {
        var lit = _definition.McduAnnunciators(_unit);
        bool seen = _lit.TryGetValue(_unit, out var before);
        _lit[_unit] = lit;
        if (_rendered is { IsBlank: false })
            _statusBox.Text = A300McduLights.Status(UnitName, lit);
        if (!silent && seen && A300McduLights.ComingOn(before!, lit, _unit) is { } on)
            _announcer.Announce(on);
    }

    private string UnitName => _unit == A300McduUnit.Captain ? "Captain MCDU" : "First Officer MCDU";

    private void Render(A300McduScreen screen, bool silent)
    {
        _rendered = screen;
        if (screen.IsBlank)
        {
            _rows = null;
            _display.SetLines(new[] { $"{UnitName} is blank. It may be unpowered." });
            _statusBox.Text = $"{UnitName}: blank";
            return;
        }
        _statusBox.Text = A300McduLights.Status(UnitName, _definition.McduAnnunciators(_unit));

        var previous = CursorRow();
        var rows = A300McduRows.Build(screen);
        _display.SetLines(rows.Select(r => r.Text).ToList());
        _rows = rows;

        string title = screen.Title.Trim();
        bool changed = !_lastTitle.TryGetValue(_unit, out var last) || last != title;
        _lastTitle[_unit] = title;
        int index = changed ? A300McduRows.PageStart(rows) : A300McduRows.Restore(rows, previous);
        if (changed && !silent && title.Length > 0)
            _announcer.Announce(title);
        if (index < 0 && _display.SelectedIndex < 0)
            index = A300McduRows.PageStart(rows);
        if (index >= 0 && index < _display.Items.Count && _display.SelectedIndex != index)
            _display.SelectedIndex = index;
    }

    private A300McduRow? CursorRow()
    {
        int i = _display.SelectedIndex;
        return _rows != null && i >= 0 && i < _rows.Count ? _rows[i] : null;
    }

    /// <summary>Shows the selected unit's current page without speaking it, and re-seeds the
    /// scratchpad read-back: a unit switch or a re-show is not news.</summary>
    private void Resync()
    {
        _rows = null;
        _rendered = null;
        _scratchpad.Reset();
        var screen = _sim.A300McduDataManager?.GetScreen(_unit);
        if (screen == null)
        {
            ShowWaiting();
            return;
        }
        // Following a unit anew starts its blank clock again: while the window was not polling it could not
        // see how long the unit had been blank. A blank not yet judged shows the waiting row, never "blank"
        // (it may be a page change).
        _blank.Reset();
        if (_blank.Follow(_unit, screen, DateTime.UtcNow) == A300McduDisplayAction.ShowWaiting)
            ShowWaiting();
        else
            Render(screen, silent: true);
        _scratchpad.OnPoll(screen.Scratchpad.Trim(), DateTime.UtcNow);
        UpdateAnnunciators(silent: true);
    }

    private void ShowWaiting()
    {
        _display.SetLines(new[] { $"{UnitName}: waiting for data" });
        _statusBox.Text = $"{UnitName}: waiting for data";
    }

    // ---------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        if (!Visible)
        {
            // The export does not survive a flight load; switching it on is idempotent.
            _definition.EnsureMcduExport(force: true);
            Resync();
            _poll.Start();
            // A first open names the page through the screen reader's own read of the focused row.
            if (_rows != null && _display.Items.Count > 0)
                _display.SelectedIndex = 0;
        }
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        ActiveControl = _display;
        _display.Focus();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
            _poll.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poll.Stop();
            _poll.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// The A300 MCDU window's form-wide keys, as a pure function of the chord, what has focus and the
/// line-select layout setting. Same two rules as the MD-11 window's: a chord the focused unit
/// selector needs is never taken (<see cref="MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting.ComboBoxNeedsKey"/>),
/// and a binding matches its own chord and nothing more.
/// </summary>
public static class A300McduKeyRouting
{
    public enum ComboFocus { None, Closed, Dropped }

    public enum ActionKind { Pass, Press, Close, SelectUnit, FocusInput, FocusDisplay }

    public readonly record struct Action(ActionKind Kind, string Key = "", A300McduUnit Unit = A300McduUnit.Captain)
    {
        public static readonly Action Pass = new(ActionKind.Pass);
        public static Action Press(string key) => new(ActionKind.Press, key);
    }

    public static Action Resolve(Keys keyCode, bool alt, bool control, bool shift, ComboFocus combo, bool useAlternateLskKeys)
    {
        if (combo != ComboFocus.None
            && MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting.ComboBoxNeedsKey(keyCode, alt, control, shift, combo == ComboFocus.Dropped))
            return Action.Pass;

        bool none = !alt && !control && !shift;
        if (keyCode == Keys.Escape && none)
            return new Action(ActionKind.Close);

        // Line-select keys, two layouts (FMC Settings): Ctrl+1..6 left / Alt+1..6 right, or F1..F12.
        if (useAlternateLskKeys)
        {
            if (none && keyCode >= Keys.F1 && keyCode <= Keys.F6)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.F1 + 1, right: false));
            if (none && keyCode >= Keys.F7 && keyCode <= Keys.F12)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.F7 + 1, right: true));
        }
        else
        {
            if (control && !alt && !shift && keyCode >= Keys.D1 && keyCode <= Keys.D6)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.D1 + 1, right: false));
            if (alt && !control && !shift && keyCode >= Keys.D1 && keyCode <= Keys.D6)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.D1 + 1, right: true));
        }

        // Slew: the unmodified page keys move through the content being read, as on every CDU window.
        if (none && keyCode == Keys.PageUp) return Action.Press("UARROW");
        if (none && keyCode == Keys.PageDown) return Action.Press("DOWN");

        // The Alt-only chords. Alt+Up and Alt+Down slew too, as in the MD-11, FBW and Fenix MCDU windows; they sit
        // after the combo guard above because a focused unit selector needs both to open and close its list.
        if (alt && !control && !shift)
        {
            switch (keyCode)
            {
                case Keys.Up: return Action.Press("UARROW");
                case Keys.Down: return Action.Press("DOWN");
                case Keys.Right: return Action.Press("NEXT");
                case Keys.S: return new Action(ActionKind.FocusInput);
                case Keys.Home: return new Action(ActionKind.FocusDisplay);
            }
        }

        if (control && shift && !alt)
        {
            switch (keyCode)
            {
                case Keys.L: return new Action(ActionKind.SelectUnit, Unit: A300McduUnit.Captain);
                case Keys.R: return new Action(ActionKind.SelectUnit, Unit: A300McduUnit.FirstOfficer);
            }
        }
        return Action.Pass;
    }
}
